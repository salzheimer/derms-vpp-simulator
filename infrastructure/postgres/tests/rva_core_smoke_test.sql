/*
Smoke test for rva_core_init.sql: versioning (including adding a column to a versioned table), as-of reads,
actor/reason capture, grid node identity, key constraints, and the simulation event and immutability triggers. Runs in one transaction and rolls back, leaving no data behind.

  docker exec -i rva_core_db psql -U postgres -d rva_core -v ON_ERROR_STOP=1 < infrastructure/postgres/tests/rva_core_smoke_test.sql

Prints "rva_core smoke test: PASS" on success; any failed check raises an error and stops the script.
*/

BEGIN;

-- Assertion helper, created in pg_temp so it disappears with the session.
CREATE FUNCTION pg_temp.check(ok boolean, message text) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    IF ok IS NOT TRUE THEN
        RAISE EXCEPTION 'FAILED: %', message;
    END IF;
END $$;

DO $$
DECLARE
    system_actor    constant uuid := '00000000-0000-7000-8000-000000000001';
    before_update   timestamptz;
    v_substation    bigint;
    v_feeder        bigint;
    v_transformer   bigint;
    v_premise       bigint;
    v_profile       int;
    v_pv_profile    int;
    v_asset         bigint;
    v_component     bigint;
    v_run           uuid;
    v_node          bigint;
    v_c_and_i       bigint;
    before_alter    timestamptz;
    n               int;
    r               record;
BEGIN
    PERFORM set_config('rva.actor', system_actor::text, true);
    PERFORM set_config('rva.actor_type', 'system', true);

    -- ---------------------------------------------------------------- topology: a minimal Scenario A
    INSERT INTO topology.substations (substation_code, rated_capacity_kva)
    VALUES ('SMOKE-SUB', 30000) RETURNING substation_id INTO v_substation;
    INSERT INTO topology.feeders (substation_id, feeder_code, nominal_voltage_kv, rated_capacity_kva)
    VALUES (v_substation, 'SMOKE-FDR', 12.47, 8000) RETURNING feeder_id INTO v_feeder;
    INSERT INTO topology.neighborhood_transformers (feeder_id, neighborhood_transformer_code, rated_capacity_kva, secondary_voltage_v)
    VALUES (v_feeder, 'SMOKE-XFMR', 25, 240) RETURNING neighborhood_transformer_id INTO v_transformer;
    INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id, service_capacity_kva, can_island)
    SELECT v_transformer, 'SMOKE-PREM', customer_segment_id, 48, TRUE FROM topology.customer_segments WHERE internal_code = 'residential'
    RETURNING premise_id INTO v_premise;

    INSERT INTO topology.hardware_profiles (hardware_profile_code, manufacturer, model, technology_type_id, rated_kw, rated_kva, rated_kwh, round_trip_efficiency)
    SELECT 'smoke_powerwall_3', 'Tesla', 'Powerwall 3', technology_type_id, 11.5, 11.5, 13.5, 0.90
    FROM topology.technology_types WHERE internal_code = 'bess'
    RETURNING hardware_profile_id INTO v_profile;
    INSERT INTO topology.hardware_profiles (hardware_profile_code, manufacturer, model, technology_type_id, rated_kw)
    SELECT 'smoke_pv_module_400w', 'Generic', 'PV Module 400 W', technology_type_id, 0.4
    FROM topology.technology_types WHERE internal_code = 'solar_pv'
    RETURNING hardware_profile_id INTO v_pv_profile;

    INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, control_hardware_profile_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
    SELECT 'SMOKE-ASSET', v_premise, interconnection_type_id, v_profile, -5, 11.5, 11.5
    FROM topology.interconnection_types WHERE internal_code = 'btm'
    RETURNING asset_id INTO v_asset;
    INSERT INTO topology.asset_components (asset_id, technology_type_id, hardware_profile_id, unit_count, rated_kw, rated_kwh)
    SELECT v_asset, technology_type_id, v_profile, 1, 11.5, 13.5 FROM topology.technology_types WHERE internal_code = 'bess'
    RETURNING asset_component_id INTO v_component;

    SELECT * INTO r FROM topology.assets WHERE asset_id = v_asset;
    PERFORM pg_temp.check(r.created_at IS NOT NULL AND upper_inf(r.sys_period), 'insert stamps created_at and an open sys_period');

    -- ---------------------------------------------------------------- versioning: update archives the old version
    before_update := clock_timestamp();
    PERFORM set_config('rva.change_reason', 'interconnection amendment', true);
    UPDATE topology.assets SET export_limit_kw = 10 WHERE asset_id = v_asset;

    SELECT count(*) INTO n FROM topology.assets_history WHERE asset_id = v_asset;
    PERFORM pg_temp.check(n = 1, 'update writes exactly one history row');

    SELECT * INTO r FROM topology.assets_history WHERE asset_id = v_asset;
    PERFORM pg_temp.check(r.operation = 'UPDATE' AND r.export_limit_kw IS NULL, 'history holds the previous version');
    PERFORM pg_temp.check(r.changed_by = system_actor AND r.actor_type = 'system', 'history records the actor from SET LOCAL');
    PERFORM pg_temp.check(r.change_reason = 'interconnection amendment', 'history records the change reason');
    PERFORM pg_temp.check(NOT upper_inf(r.sys_period), 'archived version has a closed period');

    SELECT export_limit_kw INTO r FROM topology.assets_as_of(before_update) WHERE asset_id = v_asset;
    PERFORM pg_temp.check(r.export_limit_kw IS NULL, 'assets_as_of(before the update) returns the old version');
    SELECT export_limit_kw INTO r FROM topology.assets_as_of(clock_timestamp()) WHERE asset_id = v_asset;
    PERFORM pg_temp.check(r.export_limit_kw = 10, 'assets_as_of(now) returns the current version');

    -- ---------------------------------------------------------------- versioning: no-op updates and deletes
    UPDATE topology.assets SET export_limit_kw = 10 WHERE asset_id = v_asset;
    SELECT count(*) INTO n FROM topology.assets_history WHERE asset_id = v_asset;
    PERFORM pg_temp.check(n = 1, 'a no-op update writes no history');

    UPDATE topology.assets SET created_at = '2000-01-01' WHERE asset_id = v_asset;
    SELECT count(*) INTO n FROM topology.assets WHERE asset_id = v_asset AND created_at > '2000-01-01';
    PERFORM pg_temp.check(n = 1, 'created_at cannot be changed by the application');

    DELETE FROM topology.asset_components WHERE asset_component_id = v_component;
    SELECT count(*) INTO n FROM topology.asset_components_history WHERE asset_component_id = v_component AND operation = 'DELETE';
    PERFORM pg_temp.check(n = 1, 'delete archives the removed row');

    -- ---------------------------------------------------------------- grid node identity
    SELECT gt.internal_code INTO r FROM topology.grid_nodes n JOIN topology.grid_node_types gt USING (grid_node_type_id)
    WHERE n.grid_node_id = (SELECT grid_node_id FROM topology.neighborhood_transformers WHERE neighborhood_transformer_id = v_transformer);
    PERFORM pg_temp.check(r.internal_code = 'neighborhood_transformer', 'a new node gets a grid_node_id of its own type');

    SELECT count(DISTINCT grid_node_id) INTO n FROM (
        SELECT grid_node_id FROM topology.substations WHERE substation_id = v_substation
        UNION ALL SELECT grid_node_id FROM topology.feeders WHERE feeder_id = v_feeder
        UNION ALL SELECT grid_node_id FROM topology.neighborhood_transformers WHERE neighborhood_transformer_id = v_transformer
        UNION ALL SELECT grid_node_id FROM topology.premises WHERE premise_id = v_premise) ids;
    PERFORM pg_temp.check(n = 4, 'grid_node_ids are unique across levels');

    SELECT grid_node_id INTO v_node FROM topology.feeders WHERE feeder_id = v_feeder;
    UPDATE topology.feeders SET grid_node_id = v_node + 1000 WHERE feeder_id = v_feeder;
    SELECT count(*) INTO n FROM topology.feeders WHERE feeder_id = v_feeder AND grid_node_id = v_node;
    PERFORM pg_temp.check(n = 1, 'a node''s grid_node_id cannot be changed');
    SELECT count(*) INTO n FROM topology.feeders_history WHERE feeder_id = v_feeder;
    PERFORM pg_temp.check(n = 0, 'an attempted grid_node_id change writes no history');

    BEGIN
        DELETE FROM topology.grid_nodes WHERE grid_node_id = v_node;
        PERFORM pg_temp.check(false, 'grid nodes cannot be deleted');
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM LIKE 'FAILED:%' THEN RAISE; END IF;
    END;

    -- primary-metered C&I premise served directly from the feeder, and an FTM battery on a transformer secondary
    INSERT INTO topology.premises (feeder_id, premise_code, customer_segment_id)
    SELECT v_feeder, 'SMOKE-PREM-PRIMARY', customer_segment_id FROM topology.customer_segments WHERE internal_code = 'commercial_industrial'
    RETURNING premise_id INTO v_c_and_i;
    INSERT INTO topology.assets (asset_code, neighborhood_transformer_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
    SELECT 'SMOKE-XFMR-BATTERY', v_transformer, interconnection_type_id, -25, 25, 25 FROM topology.interconnection_types WHERE internal_code = 'ftm';

    SELECT grid_node_id INTO v_node FROM topology.premises WHERE premise_id = v_c_and_i;
    DELETE FROM topology.premises WHERE premise_id = v_c_and_i;
    SELECT count(*) INTO n FROM topology.grid_nodes WHERE grid_node_id = v_node;
    PERFORM pg_temp.check(n = 1, 'a deleted node''s grid_node_id stays registered');

    -- ---------------------------------------------------------------- constraints
    BEGIN
        INSERT INTO topology.assets (asset_code, premise_id, feeder_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
        SELECT 'SMOKE-BAD-1', v_premise, v_feeder, interconnection_type_id, 0, 5, 5 FROM topology.interconnection_types WHERE internal_code = 'btm';
        PERFORM pg_temp.check(false, 'an asset with two connection points is rejected');
    EXCEPTION WHEN check_violation THEN NULL;
    END;

    BEGIN
        INSERT INTO topology.premises (neighborhood_transformer_id, feeder_id, premise_code, customer_segment_id)
        SELECT v_transformer, v_feeder, 'SMOKE-BAD-PREM', customer_segment_id FROM topology.customer_segments WHERE internal_code = 'residential';
        PERFORM pg_temp.check(false, 'a premise with two service points is rejected');
    EXCEPTION WHEN check_violation THEN NULL;
    END;

    BEGIN
        INSERT INTO topology.assets (asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
        SELECT 'SMOKE-BAD-2', v_premise, interconnection_type_id, 0, 20, 10 FROM topology.interconnection_types WHERE internal_code = 'btm';
        PERFORM pg_temp.check(false, 'an asset whose power exceeds its kVA rating is rejected');
    EXCEPTION WHEN check_violation THEN NULL;
    END;

    BEGIN
        INSERT INTO topology.asset_components (asset_id, technology_type_id, hardware_profile_id)
        SELECT v_asset, technology_type_id, v_profile FROM topology.technology_types WHERE internal_code = 'solar_pv';
        PERFORM pg_temp.check(false, 'a PV component pointing at a battery profile is rejected');
    EXCEPTION WHEN foreign_key_violation THEN NULL;
    END;

    INSERT INTO topology.asset_components (asset_id, technology_type_id, hardware_profile_id, unit_count, rated_kw)
    SELECT v_asset, technology_type_id, v_pv_profile, 20, 8.0 FROM topology.technology_types WHERE internal_code = 'solar_pv';

    -- ---------------------------------------------------------------- versioning: adding a column later
    -- Columns are appended to the history table after the audit columns, so archiving must match by name.
    ALTER TABLE topology.substations ADD COLUMN smoke_note VARCHAR(50);

    BEGIN
        UPDATE topology.substations SET rated_capacity_kva = 31000 WHERE substation_id = v_substation;
        PERFORM pg_temp.check(false, 'an update fails loudly when enable_versioning was not re-run after ALTER TABLE');
    EXCEPTION WHEN undefined_column THEN NULL;
    END;

    PERFORM topology.enable_versioning('substations');
    before_alter := clock_timestamp();
    EXECUTE 'UPDATE topology.substations SET smoke_note = ''uprated'', rated_capacity_kva = 32000 WHERE substation_id = $1' USING v_substation;

    EXECUTE 'SELECT operation, rated_capacity_kva, smoke_note FROM topology.substations_history WHERE substation_id = $1' INTO r USING v_substation;
    PERFORM pg_temp.check(r.operation = 'UPDATE' AND r.rated_capacity_kva = 30000 AND r.smoke_note IS NULL,
        'after enable_versioning, history archives a table with an added column by name');
    EXECUTE 'SELECT rated_capacity_kva, smoke_note FROM topology.substations_as_of($1) WHERE substation_id = $2' INTO r USING before_alter, v_substation;
    PERFORM pg_temp.check(r.rated_capacity_kva = 30000 AND r.smoke_note IS NULL, 'as_of works after a column is added');
    EXECUTE 'SELECT smoke_note FROM topology.substations_as_of(clock_timestamp()) WHERE substation_id = $1' INTO r USING v_substation;
    PERFORM pg_temp.check(r.smoke_note = 'uprated', 'as_of(now) returns the added column');

    -- ---------------------------------------------------------------- simulation: lifecycle events
    PERFORM set_config('rva.change_reason', 'smoke test run created', true);
    INSERT INTO simulation.simulation_runs (scenario_name, run_kind_id, status_type_id, scope_level_id, scope_root_code, detail_level_id, initial_state_as_of)
    SELECT 'smoke', k.run_kind_id, s.status_type_id, l.scope_level_id, 'SMOKE-XFMR', d.detail_level_id, before_update
    FROM simulation.run_kinds k, simulation.simulation_run_status_types s, simulation.scope_levels l, simulation.detail_levels d
    WHERE k.internal_code = 'chaos' AND s.name = 'Pending' AND l.internal_code = 'neighborhood_transformer' AND d.internal_code = 'assets_with_components'
    RETURNING simulation_run_id INTO v_run;

    SELECT * INTO r FROM simulation.simulation_runs WHERE simulation_run_id = v_run;
    PERFORM pg_temp.check(r.topology_as_of = r.created_at, 'topology_as_of defaults to created_at');

    UPDATE simulation.simulation_runs SET initial_state_as_of = NULL, run_kind_id = run_kind_id + 1 WHERE simulation_run_id = v_run;
    SELECT * INTO r FROM simulation.simulation_runs WHERE simulation_run_id = v_run;
    PERFORM pg_temp.check(r.initial_state_as_of = before_update AND r.run_kind_id = (SELECT run_kind_id FROM simulation.run_kinds WHERE internal_code = 'chaos'),
        'run kind and initial state are fixed at creation');

    SELECT count(*) INTO n FROM simulation.simulation_run_events WHERE simulation_run_id = v_run;
    PERFORM pg_temp.check(n = 1, 'creating a run records its first event');

    INSERT INTO simulation.simulation_asset_states (simulation_run_id, asset_id, is_available)
    VALUES (v_run, v_asset, FALSE);

    PERFORM set_config('rva.change_reason', 'worker allocated', true);
    UPDATE simulation.simulation_runs
    SET status_type_id = (SELECT status_type_id FROM simulation.simulation_run_status_types WHERE name = 'Running'),
        started_at = clock_timestamp()
    WHERE simulation_run_id = v_run;

    SELECT count(*) INTO n FROM simulation.simulation_run_events WHERE simulation_run_id = v_run AND reason = 'worker allocated';
    PERFORM pg_temp.check(n = 1, 'a status change records an event with its reason');

    UPDATE simulation.simulation_runs SET scenario_name = 'smoke (renamed)' WHERE simulation_run_id = v_run;
    SELECT count(*) INTO n FROM simulation.simulation_run_events WHERE simulation_run_id = v_run;
    PERFORM pg_temp.check(n = 2, 'a change that is not a status change records no event');

    BEGIN
        INSERT INTO simulation.simulation_asset_states (simulation_run_id, asset_id) VALUES (v_run, v_asset + 1);
        PERFORM pg_temp.check(false, 'overrides cannot be added once the run is Running');
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM LIKE 'FAILED:%' THEN RAISE; END IF;
    END;

    BEGIN
        UPDATE simulation.simulation_run_events SET reason = 'tampered' WHERE simulation_run_id = v_run;
        PERFORM pg_temp.check(false, 'run events are append-only');
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM LIKE 'FAILED:%' THEN RAISE; END IF;
    END;

    RAISE NOTICE 'rva_core smoke test: PASS';
END $$;

ROLLBACK;
