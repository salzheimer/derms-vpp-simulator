/*
Fixture D - community microgrid (model-validation-scenarios.md). Codes start with FXD-. Synthetic values; test-only.

  FXD-SUB (20 MVA)
  ├── FXD-FDR (6 MVA)  ── microgrid FXD-MG, PCC on this feeder
  │   ├── FXD-XFMR-1 (25 kVA)  FXD-HOME-01..05, one 7.6 kVA asset each = 38 kVA  <- overloaded
  │   ├── FXD-XFMR-2 (75 kVA)  FXD-HOME-06..10
  │   ├── FXD-XFMR-3 (75 kVA)  FXD-HOME-11..15
  │   ├── FXD-XFMR-4 (75 kVA)  FXD-HOME-16..20
  │   ├── FXD-XFMR-SCH (300 kVA)  FXD-SCHOOL (C&I): FXD-SCHOOL-PV 100 kW, FXD-SCHOOL-BESS 150 kW
  │   └── FXD-COMM-BESS: feeder-connected FTM community battery, 500 kW
  └── FXD-FDR-2 (6 MVA): a sibling branch outside the microgrid
      └── FXD-XFMR-OTHER (50 kVA)  FXD-OTHER-HOME, asset FXD-OTHER-HOME-BESS

Homes alternate between two vendors' control equipment; every asset on FXD-FDR is a microgrid member. Feeder total
(20 x 7.6 + 100 + 150 + 500 = 902 kVA) is far below the feeder and substation ratings, so only FXD-XFMR-1 binds.
*/
DO $$
DECLARE
    v_sub   bigint;
    v_fdr   bigint;
    v_fdr2  bigint;
    v_xfmr  bigint;
    v_prem  bigint;
    v_mg    bigint;
    v_asset bigint;
    i       int;
BEGIN
    INSERT INTO topology.substations (substation_code, rated_capacity_kva) VALUES ('FXD-SUB', 20000)
    RETURNING substation_id INTO v_sub;
    INSERT INTO topology.feeders (substation_id, feeder_code, nominal_voltage_kv, rated_capacity_kva)
    VALUES (v_sub, 'FXD-FDR', 12.47, 6000) RETURNING feeder_id INTO v_fdr;
    INSERT INTO topology.feeders (substation_id, feeder_code, nominal_voltage_kv, rated_capacity_kva)
    VALUES (v_sub, 'FXD-FDR-2', 12.47, 6000) RETURNING feeder_id INTO v_fdr2;
    INSERT INTO topology.microgrids (microgrid_code, feeder_id, can_island, import_limit_kw, export_limit_kw)
    VALUES ('FXD-MG', v_fdr, TRUE, 1000, 500) RETURNING microgrid_id INTO v_mg;

    INSERT INTO topology.hardware_profiles (hardware_profile_code, manufacturer, model, rated_kw, rated_kva) VALUES
        ('fxd_vendor_a_inverter', 'VendorA', 'Home Inverter 7.6', 7.6, 7.6),
        ('fxd_vendor_b_inverter', 'VendorB', 'Home Inverter 7.6', 7.6, 7.6),
        ('fxd_utility_pcs_500',   'VendorC', 'Utility PCS 500', 500, 500);

    INSERT INTO topology.neighborhood_transformers (feeder_id, neighborhood_transformer_code, rated_capacity_kva, secondary_voltage_v)
    VALUES (v_fdr, 'FXD-XFMR-1', 25, 240), (v_fdr, 'FXD-XFMR-2', 75, 240), (v_fdr, 'FXD-XFMR-3', 75, 240),
           (v_fdr, 'FXD-XFMR-4', 75, 240), (v_fdr, 'FXD-XFMR-SCH', 300, 480), (v_fdr2, 'FXD-XFMR-OTHER', 50, 240);

    FOR i IN 1..20 LOOP
        INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id, service_capacity_kva)
        VALUES (pg_temp.transformer_id('FXD-XFMR-' || ((i - 1) / 5 + 1)), format('FXD-HOME-%s', lpad(i::text, 2, '0')),
                pg_temp.segment('residential'), 48)
        RETURNING premise_id INTO v_prem;
        INSERT INTO topology.assets (asset_lifecycle_state_id, participation_state_id, in_service_date, asset_code, premise_id, microgrid_id, interconnection_type_id, control_hardware_profile_id,
                                     rated_p_min_kw, rated_p_max_kw, rated_kva)
        VALUES (topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01', format('FXD-HOME-%s-BESS', lpad(i::text, 2, '0')), v_prem, v_mg, pg_temp.interconnection('btm'),
                pg_temp.profile(CASE WHEN i % 2 = 0 THEN 'fxd_vendor_b_inverter' ELSE 'fxd_vendor_a_inverter' END),
                -7.6, 7.6, 7.6);
    END LOOP;

    INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id, service_capacity_kva, can_island)
    VALUES (pg_temp.transformer_id('FXD-XFMR-SCH'), 'FXD-SCHOOL', pg_temp.segment('commercial_industrial'), 250, TRUE)
    RETURNING premise_id INTO v_prem;
    INSERT INTO topology.assets (asset_lifecycle_state_id, participation_state_id, in_service_date, asset_code, premise_id, microgrid_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
    VALUES (topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01', 'FXD-SCHOOL-PV',   v_prem, v_mg, pg_temp.interconnection('btm'), 0, 100, 100),
           (topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01', 'FXD-SCHOOL-BESS', v_prem, v_mg, pg_temp.interconnection('btm'), -150, 150, 150);

    INSERT INTO topology.assets (asset_lifecycle_state_id, participation_state_id, in_service_date, asset_code, feeder_id, microgrid_id, interconnection_type_id, control_hardware_profile_id,
                                 rated_p_min_kw, rated_p_max_kw, rated_kva)
    VALUES (topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01', 'FXD-COMM-BESS', v_fdr, v_mg, pg_temp.interconnection('ftm'), pg_temp.profile('fxd_utility_pcs_500'), -500, 500, 500)
    RETURNING asset_id INTO v_asset;
    INSERT INTO topology.asset_components (asset_id, technology_type_id, unit_count, rated_kw, rated_kwh)
    VALUES (v_asset, pg_temp.technology('bess'), 4, 500, 2000);

    INSERT INTO topology.premises (neighborhood_transformer_id, premise_code, customer_segment_id)
    VALUES (pg_temp.transformer_id('FXD-XFMR-OTHER'), 'FXD-OTHER-HOME', pg_temp.segment('residential'))
    RETURNING premise_id INTO v_prem;
    INSERT INTO topology.assets (asset_lifecycle_state_id, participation_state_id, in_service_date, asset_code, premise_id, interconnection_type_id, rated_p_min_kw, rated_p_max_kw, rated_kva)
    VALUES (topology.asset_lifecycle_state_id('commissioned'), topology.participation_state_id('active'), DATE '2024-06-01', 'FXD-OTHER-HOME-BESS', v_prem, pg_temp.interconnection('btm'), -5, 5, 5);
END $$;
