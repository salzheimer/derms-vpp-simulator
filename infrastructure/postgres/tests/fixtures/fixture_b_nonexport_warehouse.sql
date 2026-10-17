/*
Fixture B - non-export warehouse (model-validation-scenarios.md). One C&I premise with a zero site export limit that
can island, and two independently controlled assets: a 100 kW PV system and a 125 kW / 500 kWh battery, each with its
own control equipment. Codes start with FXB-. Synthetic values; test-only.
*/
DO $$
DECLARE
    v_sub   bigint;
    v_fdr   bigint;
    v_xfmr  bigint;
    v_prem  bigint;
    v_pv    bigint;
    v_bess  bigint;
BEGIN
    INSERT INTO topology.substations (substation_code, rated_capacity_kva) VALUES ('FXB-SUB', 30000)
    RETURNING substation_id INTO v_sub;
    INSERT INTO topology.feeders (substation_id, feeder_code, nominal_voltage_kv, rated_capacity_kva)
    VALUES (v_sub, 'FXB-FDR', 12.47, 8000) RETURNING feeder_id INTO v_fdr;
    INSERT INTO topology.neighborhood_transformers (feeder_id, neighborhood_transformer_code, rated_capacity_kva, secondary_voltage_v)
    VALUES (v_fdr, 'FXB-XFMR', 500, 480) RETURNING neighborhood_transformer_id INTO v_xfmr;
    INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id, service_capacity_kva, export_limit_kw, can_island)
    VALUES (v_xfmr, 'FXB-PREM', pg_temp.segment('commercial_industrial'), 300, 0, TRUE) RETURNING premise_id INTO v_prem;

    INSERT INTO topology.hardware_profiles (hardware_profile_code, manufacturer, model, rated_kw, rated_kva) VALUES
        ('fxb_pv_inverter_100', 'FixtureCo', 'PV Inverter 100', 100, 100),
        ('fxb_battery_pcs_125', 'OtherCo', 'Battery PCS 125', 125, 125);
    INSERT INTO topology.hardware_profiles (hardware_profile_code, manufacturer, model, technology_type_id, rated_kw, rated_kwh) VALUES
        ('fxb_battery_rack_125', 'OtherCo', 'Battery Rack 500', pg_temp.technology('bess'), 125, 500);

    INSERT INTO topology.assets (asset_lifecycle_state_id, participation_state_id, in_service_date, asset_code, premise_id, interconnection_type_id, control_hardware_profile_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
    VALUES (topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01', 'FXB-PV', v_prem, pg_temp.interconnection('btm'), pg_temp.profile('fxb_pv_inverter_100'), 0, 100, 100)
    RETURNING asset_id INTO v_pv;
    INSERT INTO topology.assets (asset_lifecycle_state_id, participation_state_id, in_service_date, asset_code, premise_id, interconnection_type_id, control_hardware_profile_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
    VALUES (topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01', 'FXB-BESS', v_prem, pg_temp.interconnection('btm'), pg_temp.profile('fxb_battery_pcs_125'), -125, 125, 125)
    RETURNING asset_id INTO v_bess;

    INSERT INTO topology.asset_components (asset_id, technology_type_id, unit_count, rated_kw) VALUES
        (v_pv, pg_temp.technology('solar_pv'), 1, 100);
    INSERT INTO topology.asset_components (asset_id, technology_type_id, hardware_profile_id, unit_count, rated_kw, rated_kwh) VALUES
        (v_bess, pg_temp.technology('bess'), pg_temp.profile('fxb_battery_rack_125'), 1, 125, 500);
END $$;
