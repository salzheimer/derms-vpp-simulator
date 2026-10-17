/*
Fixture C - large residential system (model-validation-scenarios.md). One residential premise with three 11.5 kW
battery assets (34.5 kW together) and a 10 kW site export cap; transformer, feeder, and substation ratings are far
above the fleet so only the site cap binds. Codes start with FXC-. Synthetic values; test-only.
*/
DO $$
DECLARE
    v_sub   bigint;
    v_fdr   bigint;
    v_xfmr  bigint;
    v_prem  bigint;
    v_asset bigint;
    i       int;
BEGIN
    INSERT INTO topology.substations (substation_code, rated_capacity_kva) VALUES ('FXC-SUB', 30000)
    RETURNING substation_id INTO v_sub;
    INSERT INTO topology.feeders (substation_id, feeder_code, nominal_voltage_kv, rated_capacity_kva)
    VALUES (v_sub, 'FXC-FDR', 12.47, 8000) RETURNING feeder_id INTO v_fdr;
    INSERT INTO topology.neighborhood_transformers (feeder_id, neighborhood_transformer_code, rated_capacity_kva, secondary_voltage_v)
    VALUES (v_fdr, 'FXC-XFMR', 100, 240) RETURNING neighborhood_transformer_id INTO v_xfmr;
    INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id, service_capacity_kva, export_limit_kw)
    VALUES (v_xfmr, 'FXC-PREM', pg_temp.segment('residential'), 96, 10) RETURNING premise_id INTO v_prem;

    INSERT INTO topology.hardware_profiles (hardware_profile_code, manufacturer, model, technology_type_id, rated_kw, rated_kva, rated_kwh, round_trip_efficiency)
    VALUES ('fxc_battery_11_5', 'FixtureCo', 'Integrated Battery 13.5', pg_temp.technology('bess'), 11.5, 11.5, 13.5, 0.90);

    FOR i IN 1..3 LOOP
        INSERT INTO topology.assets (asset_lifecycle_state_id, participation_state_id, in_service_date, asset_code, premise_id, interconnection_type_id, control_hardware_profile_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
        VALUES (topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01', 'FXC-BESS-' || i, v_prem, pg_temp.interconnection('btm'), pg_temp.profile('fxc_battery_11_5'), -11.5, 11.5, 11.5)
        RETURNING asset_id INTO v_asset;
        INSERT INTO topology.asset_components (asset_id, technology_type_id, hardware_profile_id, unit_count, rated_kw, rated_kwh)
        VALUES (v_asset, pg_temp.technology('bess'), pg_temp.profile('fxc_battery_11_5'), 1, 11.5, 13.5);
    END LOOP;
END $$;
