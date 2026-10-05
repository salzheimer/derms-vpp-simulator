/*
Smoke test for rva_core_init.sql: versioning, as-of reads, actor/reason capture, key constraints, and the
simulation event and immutability triggers. Runs in one transaction and rolls back, leaving no data behind.

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

    -- ---------------------------------------------------------------- constraints
    BEGIN
        INSERT INTO topology.assets (asset_code, premise_id, feeder_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
        SELECT 'SMOKE-BAD-1', v_premise, v_feeder, interconnection_type_id, 0, 5, 5 FROM topology.interconnection_types WHERE internal_code = 'btm';
        PERFORM pg_temp.check(false, 'an asset with two connection points is rejected');
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

    -- ---------------------------------------------------------------- simulation: lifecycle events
    PERFORM set_config('rva.change_reason', 'smoke test run created', true);
    INSERT INTO simulation.simulation_runs (scenario_name, status_type_id, scope_level_id, scope_root_code, detail_level_id)
    SELECT 'smoke', s.status_type_id, l.scope_level_id, 'SMOKE-XFMR', d.detail_level_id
    FROM simulation.simulation_run_status_types s, simulation.scope_levels l, simulation.detail_levels d
    WHERE s.name = 'Pending' AND l.internal_code = 'neighborhood_transformer' AND d.internal_code = 'assets_with_components'
    RETURNING simulation_run_id INTO v_run;

    SELECT * INTO r FROM simulation.simulation_runs WHERE simulation_run_id = v_run;
    PERFORM pg_temp.check(r.topology_as_of = r.created_at, 'topology_as_of defaults to created_at');

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
