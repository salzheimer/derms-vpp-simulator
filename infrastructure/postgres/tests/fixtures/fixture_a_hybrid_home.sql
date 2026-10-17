/*
Fixture A - hybrid home (model-validation-scenarios.md). One residential premise with one BTM asset behind a shared
7.6 kVA hybrid inverter; its battery (2 x 5 kW / 10 kWh) and PV (25 x 0.4 kW) components total 20 kW, above the
asset's grid-facing rating. Codes start with FXA-. Synthetic values; test-only, never part of a development seed.
*/
DO $$
DECLARE
    v_sub   bigint;
    v_fdr   bigint;
    v_xfmr  bigint;
    v_prem  bigint;
    v_asset bigint;
BEGIN
    INSERT INTO topology.substations (substation_code, rated_capacity_kva) VALUES ('FXA-SUB', 30000)
    RETURNING substation_id INTO v_sub;
    INSERT INTO topology.feeders (substation_id, feeder_code, nominal_voltage_kv, rated_capacity_kva)
    VALUES (v_sub, 'FXA-FDR', 12.47, 8000) RETURNING feeder_id INTO v_fdr;
    INSERT INTO topology.neighborhood_transformers (feeder_id, neighborhood_transformer_code, rated_capacity_kva, secondary_voltage_v)
    VALUES (v_fdr, 'FXA-XFMR', 50, 240) RETURNING neighborhood_transformer_id INTO v_xfmr;
    INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id, service_capacity_kva, can_island)
    VALUES (v_xfmr, 'FXA-PREM', pg_temp.segment('residential'), 48, TRUE) RETURNING premise_id INTO v_prem;

    -- control-only equipment has no technology; energy products carry theirs
    INSERT INTO topology.hardware_profiles (hardware_profile_code, manufacturer, model, rated_kw, rated_kva) VALUES
        ('fxa_hybrid_inverter_7_6', 'FixtureCo', 'Hybrid Inverter 7.6', 7.6, 7.6);
    INSERT INTO topology.hardware_profiles (hardware_profile_code, manufacturer, model, technology_type_id, rated_kw, rated_kwh, round_trip_efficiency) VALUES
        ('fxa_battery_5kw_10kwh', 'FixtureCo', 'Battery Module 10', pg_temp.technology('bess'), 5, 10, 0.90);
    INSERT INTO topology.hardware_profiles (hardware_profile_code, manufacturer, model, technology_type_id, rated_kw) VALUES
        ('fxa_pv_module_400w', 'FixtureCo', 'PV Module 400 W', pg_temp.technology('solar_pv'), 0.4);

    INSERT INTO topology.assets (asset_lifecycle_state_id, participation_state_id, in_service_date, asset_code, premise_id, interconnection_type_id, control_hardware_profile_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
    VALUES (topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01', 'FXA-ASSET', v_prem, pg_temp.interconnection('btm'), pg_temp.profile('fxa_hybrid_inverter_7_6'), -7.6, 7.6, 7.6)
    RETURNING asset_id INTO v_asset;
    INSERT INTO topology.asset_components (asset_id, technology_type_id, hardware_profile_id, unit_count, rated_kw, rated_kwh) VALUES
        (v_asset, pg_temp.technology('bess'),     pg_temp.profile('fxa_battery_5kw_10kwh'), 2,  10, 20),
        (v_asset, pg_temp.technology('solar_pv'), pg_temp.profile('fxa_pv_module_400w'),    25, 10, NULL);
END $$;
