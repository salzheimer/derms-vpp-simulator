# Test-only image for rva_core: the production base image plus pgTAP and pg_prove. Never used outside tests;
# rva_core_init.sql does not install pgtap (each test file creates the extension inside its own rolled-back transaction).
# Built and run by run_tests.sh.
FROM postgis/postgis:18-3.6

RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-18-pgtap libtap-parser-sourcehandler-pgtap-perl \
    && rm -rf /var/lib/apt/lists/*
