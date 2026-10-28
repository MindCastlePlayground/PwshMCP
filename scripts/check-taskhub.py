"""Verify the installed TaskHub daemon through the real Hub; optionally crash only its tray."""
import argparse
import json
import sys
import time
import tomllib
from pathlib import Path


class Keys:
    WORKER = 'worker'
    SERVER = 'server'
    TOKEN = 'token_file'
    ID = 'id'
    TASK = 'task_id'
    WORKER_ID = 'worker_id'
    PARAMS = 'params'
    ACTION = 'action'
    TTL = 'ttl_seconds'
    RUN = 'run_id'
    RESULT = 'result'
    DATA = 'data'
    STATE = 'state'
    PID = 'pid'
    HOST = 'host_pid'
    RESTARTS = 'restarts'
    BEFORE = 'before'
    AFTER = 'after'
    SECONDS = 'seconds'


class Check:
    TASK = 'daemon.pwshmcp'
    RUNS = '/v1/runs'
    TRAY = Path('C:/Program Files/PwshMCP/PwshMcp.Tray.exe')

    @staticmethod
    def main():
        parser = argparse.ArgumentParser(description=__doc__)
        parser.add_argument('--taskhub', type=Path, default=Path('H:/Documents/Abyss/TaskHub'))
        parser.add_argument('--output', type=Path, required=True)
        parser.add_argument('--recover', action='store_true')
        args = parser.parse_args()
        sys.path.insert(0, str(args.taskhub / 'tools'))
        from service_accept import Accept
        from deploy import Vault
        cfg = tomllib.loads((args.taskhub / 'config/worker.toml').read_text(encoding='utf-8-sig'))[Keys.WORKER]
        Accept.server = cfg[Keys.SERVER]
        Accept.token = Vault.crypt((args.taskhub / 'config' / cfg[Keys.TOKEN]).resolve().read_bytes(), True).decode()
        before = Check.status(Accept, cfg)
        report = {Keys.BEFORE: before}
        if args.recover:
            # Exact executable check; no process-tree kill, preserving an existing Broker.
            code = "$p=Get-Process -Id " + str(int(before[Keys.DATA][Keys.PID])) + "; if ($p.Path -ne '" + str(Check.TRAY) + "') {throw 'Tray path mismatch'}; Stop-Process -Id $p.Id -Force"
            started = time.monotonic()
            Accept.ps(code)
            until = started + 30
            while time.monotonic() < until:
                time.sleep(1)
                after = Check.status(Accept, cfg, running=False)
                state = after[Keys.DATA]
                if state[Keys.STATE] == 'running' and state[Keys.PID] != before[Keys.DATA][Keys.PID]:
                    assert state[Keys.HOST] == before[Keys.DATA][Keys.HOST], 'Host changed'
                    assert state[Keys.RESTARTS] > before[Keys.DATA][Keys.RESTARTS], 'Recovery count did not increase'
                    report.update({Keys.AFTER: after, Keys.SECONDS: round(time.monotonic() - started, 2)})
                    break
            else:
                raise AssertionError('Tray did not recover within 30 seconds')
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps(report, ensure_ascii=True, indent=2))

    @staticmethod
    def status(api, cfg, running=True):
        run = api.api(Check.RUNS, {Keys.TASK: Check.TASK, Keys.WORKER_ID: cfg[Keys.ID], Keys.PARAMS: {Keys.ACTION: 'status'}, Keys.TTL: 60})
        row = api.wait(run[Keys.RUN])
        data = row[Keys.RESULT][Keys.DATA]
        if running:
            assert data[Keys.STATE] == 'running' and data[Keys.PID] > 0, 'Daemon is not running'
        return {Keys.RUN: run[Keys.RUN], Keys.DATA: data}


if __name__ == '__main__':
    Check.main()
