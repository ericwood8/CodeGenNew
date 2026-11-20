#!/usr/bin/env bash
# Regenerates the InvoiceSystem samples with REPLACE=1 QUICK=1 (builds, no tests) and prints the summary lines of each.
#   CG_PW=<password> bash regenerate_samples.sh [Blazor React Angular Uno Sqlite Python MySql Pg Rust]
# Set SAMPLES to the folder that holds InvoiceSystem<Name> (default /c/Samples). WinUI3 is separate (InvoiceSystemWinUi3, run its own docs/Regenerate.sh).
# The password for the PostgreSQL and MySQL samples is only passed in the environment of the child scripts.
export REPLACE=1 QUICK=1
export PGPASSWORD="$CG_PW" MYSQL_PWD="$CG_PW"
SAMPLES="${SAMPLES:-/c/Samples}"
list=("$@"); [ ${#list[@]} -eq 0 ] && list=(Blazor React Angular Uno Sqlite Python MySql Pg Rust)
for s in "${list[@]}"; do
  echo "##### $s"
  bash "$SAMPLES/InvoiceSystem$s/docs/Regenerate.sh" 2>&1 | grep -E "^Done: |Error\(s\)|error|FAIL|failed|refused|^ok |^FAIL|Build succeeded|== [0-9]" | tail -12
  echo "##### $s exit=${PIPESTATUS[0]}"
done
