#!/bin/bash
# ci.sh status [run]   - latest (or given) run's jobs and steps
# ci.sh log JOBID      - a job's log (tail)
# ci.sh wait [run]     - wait until the run completes (up to ~9 min), then status
R=Lumotiverse/modern-voices-for-windows; A=https://api.github.com/repos/$R
unset GH_TOKEN
run() { [ -n "$1" ] && echo "$1" || curl -sS -m 20 "$A/actions/runs?per_page=1" | python3 -c "import json,sys; print(json.load(sys.stdin)['workflow_runs'][0]['id'])"; }
status() {
  local id=$(run "$1")
  curl -sS -m 20 "$A/actions/runs/$id" | python3 -c "import json,sys; r=json.load(sys.stdin); print(f\"run {r['id']} #{r['run_number']}: {r['status']} {r['conclusion']}  ({r['head_sha'][:7]})\")"
  curl -sS -m 20 "$A/actions/runs/$id/jobs" | python3 -c "
import json,sys
for j in json.load(sys.stdin)['jobs']:
    print(f\"  {j['name']:12} {j['status']:11} {j['conclusion'] or '':10} job {j['id']}\")
    for s in j['steps']:
        if s['conclusion'] not in ('success','skipped') or s['status']!='completed':
            print(f\"      {s['number']:>2} {s['name'][:60]:60} {s['status']} {s['conclusion'] or ''}\")"
}
case "$1" in
  status) status "$2" ;;
  log) curl -sSL -m 60 "$A/actions/jobs/$2/logs" | sed 's/^[0-9T:.\-]*Z //' | tail -${3:-80} ;;
  wait) id=$(run "$2"); for i in $(seq 1 54); do s=$(curl -sS -m 20 "$A/actions/runs/$id" | python3 -c "import json,sys; print(json.load(sys.stdin)['status'])"); [ "$s" = completed ] && break; sleep 10; done; status "$id" ;;
esac
