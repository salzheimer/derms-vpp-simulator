/*
Shared setup for every pgTAP test file, included with \ir right after BEGIN. Everything here is created inside the
test's transaction (pgtap itself, pg_temp functions, settings) and disappears on ROLLBACK.

  pg_temp.<lookup>(code)          id of a seeded lookup row by internal_code (or name, for run statuses)
  pg_temp.asset_paths(at)         every asset with its lifecycle, participation, connection path, the limits along it,
                                  and the grid node ids on its path, read through the topology _as_of functions:
                                  a reference query for GridTopology constraint paths
  pg_temp.scope_assets(level, code, at)   commissioned asset codes in the subtree under one node, as a run scope
                                  would load them
  pg_temp.asset_grid_states(at)   each asset's effective grid state, derived from node and asset grid state events:
                                  a reference implementation (executable spec) for GridTopology
  pg_temp.dispatch_eligible(at)   commissioned, active, energized assets (availability is operational, not here)
  pg_temp.new_run(...)            creates a Pending run
  pg_temp.transition(run, status, reason) moves a run to a status, stamping started_at / ended_at as the module would
*/

SET LOCAL client_min_messages = warning;  -- keep TAP output clean of NOTICEs (ex. from enable_versioning)
CREATE EXTENSION IF NOT EXISTS pgtap;

DO $$
BEGIN
    PERFORM set_config('rva.actor', '00000000-0000-7000-8000-000000000001', true);
    PERFORM set_config('rva.actor_type', 'system', true);
    PERFORM set_config('rva.change_reason', 'pgTAP fixture', true);
END $$;

-- ---------------------------------------------------------------- lookups
CREATE FUNCTION pg_temp.technology(code text) RETURNS int LANGUAGE sql STABLE AS
    $$ SELECT technology_type_id FROM topology.technology_types WHERE internal_code = code $$;
CREATE FUNCTION pg_temp.interconnection(code text) RETURNS int LANGUAGE sql STABLE AS
    $$ SELECT interconnection_type_id FROM topology.interconnection_types WHERE internal_code = code $$;
CREATE FUNCTION pg_temp.segment(code text) RETURNS int LANGUAGE sql STABLE AS
    $$ SELECT customer_segment_id FROM topology.customer_segments WHERE internal_code = code $$;
CREATE FUNCTION pg_temp.profile(code text) RETURNS int LANGUAGE sql STABLE AS
    $$ SELECT hardware_profile_id FROM topology.hardware_profiles WHERE hardware_profile_code = code $$;
CREATE FUNCTION pg_temp.grid_state(code text) RETURNS int LANGUAGE sql STABLE AS
    $$ SELECT grid_state_id FROM topology.grid_states WHERE internal_code = code $$;
CREATE FUNCTION pg_temp.run_status(status text) RETURNS int LANGUAGE sql STABLE AS
    $$ SELECT status_type_id FROM simulation.simulation_run_status_types WHERE name = status $$;
CREATE FUNCTION pg_temp.scope_level(code text) RETURNS int LANGUAGE sql STABLE AS
    $$ SELECT scope_level_id FROM simulation.scope_levels WHERE internal_code = code $$;
CREATE FUNCTION pg_temp.detail_level(code text) RETURNS int LANGUAGE sql STABLE AS
    $$ SELECT detail_level_id FROM simulation.detail_levels WHERE internal_code = code $$;
CREATE FUNCTION pg_temp.run_kind(code text) RETURNS int LANGUAGE sql STABLE AS
    $$ SELECT run_kind_id FROM simulation.run_kinds WHERE internal_code = code $$;

-- Row ids by natural code
CREATE FUNCTION pg_temp.asset_id(code text) RETURNS bigint LANGUAGE sql STABLE AS
    $$ SELECT asset_id FROM topology.assets WHERE asset_code = code $$;
CREATE FUNCTION pg_temp.premise_id(code text) RETURNS bigint LANGUAGE sql STABLE AS
    $$ SELECT premise_id FROM topology.premises WHERE premise_code = code $$;
CREATE FUNCTION pg_temp.transformer_id(code text) RETURNS bigint LANGUAGE sql STABLE AS
    $$ SELECT neighborhood_transformer_id FROM topology.neighborhood_transformers WHERE neighborhood_transformer_code = code $$;
CREATE FUNCTION pg_temp.feeder_id(code text) RETURNS bigint LANGUAGE sql STABLE AS
    $$ SELECT feeder_id FROM topology.feeders WHERE feeder_code = code $$;
CREATE FUNCTION pg_temp.substation_id(code text) RETURNS bigint LANGUAGE sql STABLE AS
    $$ SELECT substation_id FROM topology.substations WHERE substation_code = code $$;
CREATE FUNCTION pg_temp.microgrid_id(code text) RETURNS bigint LANGUAGE sql STABLE AS
    $$ SELECT microgrid_id FROM topology.microgrids WHERE microgrid_code = code $$;
CREATE FUNCTION pg_temp.node(level text, code text) RETURNS bigint LANGUAGE sql STABLE AS $$
    SELECT CASE level
               WHEN 'substation'               THEN (SELECT grid_node_id FROM topology.substations WHERE substation_code = code)
               WHEN 'feeder'                   THEN (SELECT grid_node_id FROM topology.feeders WHERE feeder_code = code)
               WHEN 'neighborhood_transformer' THEN (SELECT grid_node_id FROM topology.neighborhood_transformers WHERE neighborhood_transformer_code = code)
               WHEN 'premise'                  THEN (SELECT grid_node_id FROM topology.premises WHERE premise_code = code)
               WHEN 'microgrid'                THEN (SELECT grid_node_id FROM topology.microgrids WHERE microgrid_code = code)
           END
$$;

-- Records a grid state event for a node (level, code) or, with level 'asset', for an asset.
CREATE FUNCTION pg_temp.record_grid_state(level text, code text, state text, occurred_at timestamptz DEFAULT clock_timestamp(),
                                          reason text DEFAULT 'pgTAP grid state') RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    PERFORM set_config('rva.change_reason', reason, true);
    INSERT INTO topology.grid_state_events (grid_node_id, asset_id, grid_state_id, occurred_at)
    VALUES (CASE WHEN level <> 'asset' THEN pg_temp.node(level, code) END,
            CASE WHEN level = 'asset' THEN pg_temp.asset_id(code) END,
            pg_temp.grid_state(state), occurred_at);
END $$;

-- ---------------------------------------------------------------- topology reference queries
-- Each asset's connection path at a point in time, resolved through the _as_of functions so it works for any
-- topology version. Levels the asset sits above are NULL (a feeder-connected asset has no premise or transformer).
CREATE FUNCTION pg_temp.asset_paths(at timestamptz)
RETURNS TABLE (asset_code text, asset_id bigint, lifecycle text, participation text,
               rated_kva double precision, rated_p_max_kw double precision,
               asset_export_limit_kw double precision, interconnection text,
               premise_code text, premise_export_limit_kw double precision,
               transformer_code text, transformer_kva double precision,
               feeder_code text, feeder_kva double precision,
               substation_code text, substation_kva double precision,
               microgrid_code text, microgrid_feeder_code text, microgrid_export_limit_kw double precision,
               substation_node bigint, feeder_node bigint, microgrid_node bigint, transformer_node bigint, premise_node bigint)
LANGUAGE sql STABLE AS $$
    SELECT a.asset_code, a.asset_id, ls.internal_code, ps.internal_code, a.rated_kva, a.rated_p_max_kw, a.export_limit_kw, it.internal_code,
           p.premise_code, p.export_limit_kw,
           x.neighborhood_transformer_code, x.rated_capacity_kva,
           f.feeder_code, f.rated_capacity_kva,
           s.substation_code, s.rated_capacity_kva,
           m.microgrid_code, mf.feeder_code, m.export_limit_kw,
           s.grid_node_id, f.grid_node_id, m.grid_node_id, x.grid_node_id, p.grid_node_id
    FROM topology.assets_as_of(at) a
    JOIN topology.interconnection_types it ON it.interconnection_type_id = a.interconnection_type_id
    JOIN topology.asset_lifecycle_states ls ON ls.asset_lifecycle_state_id = a.asset_lifecycle_state_id
    JOIN topology.participation_states ps ON ps.participation_state_id = a.participation_state_id
    LEFT JOIN topology.premises_as_of(at) p ON p.premise_id = a.premise_id
    LEFT JOIN topology.neighborhood_transformers_as_of(at) x
           ON x.neighborhood_transformer_id = COALESCE(a.neighborhood_transformer_id, p.neighborhood_transformer_id)
    LEFT JOIN topology.feeders_as_of(at) f ON f.feeder_id = COALESCE(a.feeder_id, p.feeder_id, x.feeder_id)
    LEFT JOIN topology.substations_as_of(at) s ON s.substation_id = COALESCE(a.substation_id, f.substation_id)
    LEFT JOIN topology.microgrids_as_of(at) m ON m.microgrid_id = a.microgrid_id
    LEFT JOIN topology.feeders_as_of(at) mf ON mf.feeder_id = m.feeder_id
$$;

-- Commissioned asset codes in the subtree rooted at one node, as a run with that scope would load them.
-- Planned, installed, and decommissioned assets are not part of the operating grid.
CREATE FUNCTION pg_temp.scope_assets(level text, code text, at timestamptz) RETURNS SETOF text
LANGUAGE sql STABLE AS $$
    SELECT asset_code FROM pg_temp.asset_paths(at)
    WHERE lifecycle = 'commissioned'
      AND CASE level
              WHEN 'substation'               THEN substation_code
              WHEN 'feeder'                   THEN feeder_code
              WHEN 'neighborhood_transformer' THEN transformer_code
              WHEN 'premise'                  THEN premise_code
              WHEN 'microgrid'                THEN microgrid_code
          END = code
$$;

-- Effective grid state of every asset at a point in time, from the grid state events known then
-- (topology.latest_grid_states). A node with no event is in its normal state, connected_energized. Walking the path
-- from the substation down: a de-energized or disconnected node de-energizes everything below it; an islanded node
-- (microgrid, or a premise that can island) re-energizes what is below it from local sources; a later
-- connected_energized cannot re-energize below a dead node. An asset's own disconnected state overrides its path.
CREATE FUNCTION pg_temp.asset_grid_states(at timestamptz) RETURNS TABLE (asset_code text, grid_state text)
LANGUAGE plpgsql STABLE AS $$
DECLARE
    a           record;
    node        bigint;
    node_state  text;
BEGIN
    FOR a IN SELECT * FROM pg_temp.asset_paths(at) LOOP
        asset_code := a.asset_code;
        grid_state := 'connected_energized';
        FOREACH node IN ARRAY ARRAY[a.substation_node, a.feeder_node, a.microgrid_node, a.transformer_node, a.premise_node] LOOP
            CONTINUE WHEN node IS NULL;
            node_state := (SELECT s.internal_code FROM topology.latest_grid_states(at) g JOIN topology.grid_states s USING (grid_state_id)
                           WHERE g.grid_node_id = node);
            IF node_state = 'islanded' THEN
                grid_state := 'islanded';
            ELSIF node_state IN ('connected_deenergized', 'disconnected') THEN
                grid_state := 'connected_deenergized';
            END IF;
        END LOOP;
        IF (SELECT s.internal_code FROM topology.latest_grid_states(at) g JOIN topology.grid_states s USING (grid_state_id)
            WHERE g.asset_id = a.asset_id) = 'disconnected' THEN
            grid_state := 'disconnected';
        END IF;
        RETURN NEXT;
    END LOOP;
END $$;

-- Assets dispatch may command at a point in time, as far as the registry and grid state can tell: commissioned,
-- actively participating, and energized (from the grid or an island). Real-time availability (online, maintenance)
-- comes from telemetry and is checked separately.
CREATE FUNCTION pg_temp.dispatch_eligible(at timestamptz) RETURNS SETOF text LANGUAGE sql STABLE AS $$
    SELECT p.asset_code FROM pg_temp.asset_paths(at) p JOIN pg_temp.asset_grid_states(at) g USING (asset_code)
    WHERE p.lifecycle = 'commissioned' AND p.participation = 'active' AND g.grid_state IN ('connected_energized', 'islanded')
$$;

-- ---------------------------------------------------------------- simulation helpers
CREATE FUNCTION pg_temp.new_run(level text, root_code text, detail text DEFAULT 'assets',
                                topology_as_of timestamptz DEFAULT NULL, initial_state_as_of timestamptz DEFAULT NULL,
                                simulated_window tstzrange DEFAULT NULL)
RETURNS uuid LANGUAGE sql AS $$
    INSERT INTO simulation.simulation_runs (scenario_name, run_kind_id, status_type_id, scope_level_id, scope_root_code,
                                            detail_level_id, topology_as_of, initial_state_as_of, simulated_window)
    VALUES ('pgTAP ' || root_code, pg_temp.run_kind('chaos'), pg_temp.run_status('Pending'), pg_temp.scope_level(level),
            root_code, pg_temp.detail_level(detail), topology_as_of, initial_state_as_of, simulated_window)
    RETURNING simulation_run_id
$$;

CREATE FUNCTION pg_temp.transition(run uuid, status text, reason text DEFAULT NULL) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    PERFORM set_config('rva.change_reason', coalesce(reason, ''), true);
    UPDATE simulation.simulation_runs
    SET status_type_id = pg_temp.run_status(status),
        started_at = CASE WHEN status = 'Running' THEN clock_timestamp() ELSE started_at END,
        ended_at   = CASE WHEN status IN ('Completed', 'Failed') THEN clock_timestamp() ELSE ended_at END
    WHERE simulation_run_id = run;
END $$;
