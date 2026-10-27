import json
from pathlib import Path
import subprocess
import sys
import time
import uuid


class Acceptance:
    ROOT = Path(__file__).resolve().parent.parent
    HOST = ROOT / 'artifacts/app/PwshMcp.Host.exe'
    RECORD = ROOT / 'artifacts/desktop-acceptance.json'
    CODE = "[pscustomobject]@{pid=$PID;admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)} | ConvertTo-Json -Compress"

    @classmethod
    def call(cls, **request):
        result = subprocess.run([str(cls.HOST), 'ctl'], input=json.dumps(request), capture_output=True, text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW, timeout=15)
        if result.returncode:
            raise RuntimeError(result.stderr)
        value = json.loads(result.stdout)
        if not value['ok']:
            raise RuntimeError(value['error'])
        return value

    @classmethod
    def submit(cls, client, key):
        return cls.call(op='submit', id=key, client=client, taskName='桌面管理员授权验收', label='桌面验收：仅读取管理员标志和进程号，不修改系统。批准后命令应使用不同进程且无需重复 UAC。', run=dict(description='读取当前进程的管理员标志和进程号，验证授权后的执行身份与进程隔离。', code=cls.CODE, cwd=str(cls.ROOT), privilege='elevated', timeoutSeconds=20))

    @classmethod
    def prepare(cls):
        client = 'desktop-check-' + uuid.uuid4().hex[:6]
        first = cls.submit(client, 'first')
        second = cls.submit(client, 'second')
        record = dict(client=client, grant=first['grant']['id'], tasks=[first['job']['id'], second['job']['id']])
        cls.RECORD.write_text(json.dumps(record, indent=2), encoding='utf-8')
        print(json.dumps(dict(client=client, state=first['job']['state'], grant=record['grant']), ensure_ascii=False))

    @classmethod
    def check(cls):
        record = json.loads(cls.RECORD.read_text(encoding='utf-8'))
        jobs = [cls.call(op='status', id=key)['job'] for key in record['tasks']]
        if any(job['state'] != 'succeeded' for job in jobs):
            print(json.dumps(dict(states=[job['state'] for job in jobs])))
            return
        third = cls.submit(record['client'], 'after-approval')
        assert third['job']['grantId'] == record['grant'], 'grant changed'
        for _ in range(60):
            latest = cls.call(op='status', id=third['job']['id'])['job']
            if latest['state'] not in ('queued', 'running'):
                break
            time.sleep(.25)
        assert latest['state'] == 'succeeded', latest['state']
        jobs.append(latest)
        results = [json.loads(cls.call(op='output', id=job['id'])['output']) for job in jobs]
        assert all(result['admin'] for result in results), 'not elevated'
        assert len({result['pid'] for result in results}) == 3, 'process reused'
        assert len({job['result']['environmentId'] for job in jobs}) == 3, 'environment reused'
        record['verified'] = dict(admin=True, freshProcesses=3, sameGrant=True, states=[job['state'] for job in jobs])
        cls.RECORD.write_text(json.dumps(record, indent=2), encoding='utf-8')
        print(json.dumps(record['verified']))

    @classmethod
    def run(cls):
        if len(sys.argv) > 1 and sys.argv[1] == 'prepare':
            cls.prepare()
        else:
            cls.check()


Acceptance.run()
