import json
import os
from pathlib import Path
import subprocess
import time
import uuid


class Integration:
    ROOT = Path(__file__).resolve().parent.parent
    HOST = ROOT / 'artifacts/test/PwshMcp.Host.exe'
    FLAGS = subprocess.CREATE_NO_WINDOW
    CLIENT = 'integration-' + uuid.uuid4().hex[:8]
    INVALID_NAMES = ('', 'bad\nname', 'x' * 121)

    @classmethod
    def call(cls, **request):
        proc = subprocess.run([str(cls.HOST), 'ctl'], input=json.dumps(request), capture_output=True, text=True, encoding='utf-8', creationflags=cls.FLAGS, timeout=15)
        if proc.returncode:
            raise RuntimeError(proc.stderr)
        return json.loads(proc.stdout)

    @classmethod
    def wait(cls, task):
        for _ in range(100):
            result = cls.call(op='status', id=task)
            if result['job']['state'] not in ('running', 'queued'):
                return result['job']
            time.sleep(.15)
        raise TimeoutError('task did not complete')

    @classmethod
    def run(cls):
        results = []
        processes = []
        broker = subprocess.Popen([str(cls.HOST), 'broker'], creationflags=cls.FLAGS, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        processes.append(broker)
        worker = None
        mcp = None
        try:
            time.sleep(.8)
            assert broker.poll() is None, 'isolated broker did not start'
            snap = cls.call(op='snapshot')
            assert snap['ok']
            results.append('broker pipe identity handshake')
            worker = subprocess.Popen([str(cls.HOST), 'worker', '--client', cls.CLIENT, '--parent', str(os.getpid())], creationflags=cls.FLAGS, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            processes.append(worker)
            spec = dict(description='输出集成测试标记并验证普通任务执行。', code="Write-Output 'fresh-ok'", cwd=str(cls.ROOT), privilege='normal', timeoutSeconds=20)
            omitted_description = cls.call(op='submit', id='omitted-description', client=cls.CLIENT, run={k: v for k, v in spec.items() if k != 'description'})
            assert not omitted_description['ok'] and omitted_description['error'] == 'description_required', omitted_description
            missing_description = cls.call(op='submit', id='missing-description', client=cls.CLIENT, run=dict(spec, description=''))
            assert not missing_description['ok'] and missing_description['error'] == 'description_required', missing_description
            whitespace_description = cls.call(op='submit', id='whitespace-description', client=cls.CLIENT, run=dict(spec, description='  \t'))
            assert not whitespace_description['ok'] and whitespace_description['error'] == 'description_required', whitespace_description
            no_task_name = cls.call(op='submit', id='missing-task-name', client=cls.CLIENT, run=dict(spec, privilege='elevated'))
            assert not no_task_name['ok'] and no_task_name['error'] == 'task_name_required', no_task_name
            no_access_task_name = cls.call(op='request_access', client=cls.CLIENT)
            assert not no_access_task_name['ok'] and no_access_task_name['error'] == 'task_name_required', no_access_task_name
            results.append('broker rejects missing or blank audit description and first elevated requests without taskName')
            first = cls.call(op='submit', id='normal-1', client=cls.CLIENT, run=spec)
            assert first['ok'], first
            task = first['job']['id']
            job = cls.wait(task)
            assert job['state'] == 'succeeded', job
            output = cls.call(op='output', id=task)
            assert 'fresh-ok' in output['output'], output
            results.append('normal fresh process and output')
            failed_id = cls.call(op='submit', id='nonterminating-error', client=cls.CLIENT, run=dict(spec, code="Write-Error 'expected-error-text'"))['job']['id']
            failed = cls.wait(failed_id)
            assert failed['state'] == 'failed', failed
            error = failed.get('result', {}).get('error', '')
            assert 'expected-error-text' in error, error
            results.append('non-terminating PowerShell error is failed with error text')
            duplicate = cls.call(op='submit', id='normal-1', client=cls.CLIENT, run=spec)
            assert duplicate['job']['id'] == task
            conflict = cls.call(op='submit', id='normal-1', client=cls.CLIENT, run=dict(spec, code='different'))
            assert not conflict['ok'] and conflict['error'] == 'request_id_conflict'
            results.append('idempotency and conflicting payload rejected')
            pending = cls.call(op='submit', id='admin-1', client=cls.CLIENT, taskName='集成测试管理员授权', run=dict(spec, description='验证管理员授权申请及拒绝流程。', privilege='elevated'))
            assert pending['job']['state'] == 'needsApproval'
            grant = pending['grant']['id']
            during_pending = cls.call(op='submit', id='normal-during-pending', client=cls.CLIENT, run=spec)
            assert cls.wait(during_pending['job']['id'])['state'] == 'succeeded'
            refused = cls.call(op='approve', id=grant)
            assert not refused['ok'] and refused['error'] == 'requires_user_elevation', refused
            assert cls.call(op='deny', id=grant)['ok']
            assert cls.call(op='status', id=pending['job']['id'])['job']['state'] == 'rejected'
            after_deny = cls.call(op='submit', id='normal-after-deny', client=cls.CLIENT, run=spec)
            assert cls.wait(after_deny['job']['id'])['state'] == 'succeeded'
            results.append('pending admin request does not block normal work before or after deny')
            long_job = cls.call(op='submit', id='cancel-1', client=cls.CLIENT, run=dict(spec, code='Start-Sleep -Seconds 30'))['job']['id']
            time.sleep(.8)
            cls.call(op='cancel', id=long_job)
            assert cls.wait(long_job)['state'] == 'cancelled'
            results.append('running process cancellation')
            orphan = cls.call(op='submit', id='worker-death', client=cls.CLIENT, run=dict(spec, code='Start-Sleep -Seconds 30'))['job']['id']
            time.sleep(.8)
            worker.terminate(); worker.wait(timeout=10); worker = None
            assert cls.wait(orphan)['state'] == 'outcomeUnknown'
            worker = subprocess.Popen([str(cls.HOST), 'worker', '--client', cls.CLIENT, '--parent', str(os.getpid())], creationflags=cls.FLAGS, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            processes.append(worker)
            recovered = cls.call(op='submit', id='worker-recovered', client=cls.CLIENT, run=spec)['job']['id']
            assert cls.wait(recovered)['state'] == 'succeeded'
            results.append('worker death marks running job outcomeUnknown; replacement handles new work')

            restart_pending = cls.call(op='submit', id='restart-admin', client=cls.CLIENT, taskName='重启后审计字段保留', run=dict(spec, description='验证 broker 重启后任务审计字段保留。', privilege='elevated'))
            restart_spec = dict(spec, description='验证 broker 重启时运行中任务的恢复状态。', code='Start-Sleep -Seconds 30', env={'TEST_SECRET': 'must-not-persist'})
            restart_run = cls.call(op='submit', id='restart-running', client=cls.CLIENT, run=restart_spec)
            time.sleep(.8)
            worker.terminate(); worker.wait(timeout=10); worker = None
            broker.terminate(); broker.wait(timeout=10)
            broker = subprocess.Popen([str(cls.HOST), 'broker'], creationflags=cls.FLAGS, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            processes.append(broker)
            time.sleep(.8)
            assert broker.poll() is None, 'broker did not restart'
            assert cls.call(op='status', id=restart_run['job']['id'])['job']['state'] == 'outcomeUnknown'
            snapshot = cls.call(op='snapshot')['snapshot']
            grant_state = next(g['state'] for g in snapshot['grants'] if g['id'] == restart_pending['grant']['id'])
            assert grant_state == 'expired', grant_state
            restored_grant = next(g for g in snapshot['grants'] if g['id'] == restart_pending['grant']['id'])
            restored_job = next(j for j in snapshot['jobs'] if j['id'] == restart_run['job']['id'])
            assert restored_grant['taskName'] == '重启后审计字段保留', restored_grant
            assert restored_job['run']['description'] == '验证 broker 重启时运行中任务的恢复状态。', restored_job
            assert restored_job['run']['code'] == '' and restored_job['run']['env'] == {}, restored_job
            duplicate = cls.call(op='submit', id='restart-running', client=cls.CLIENT, run=restart_spec)
            assert duplicate['job']['id'] == restart_run['job']['id']
            new_worker = subprocess.Popen([str(cls.HOST), 'worker', '--client', cls.CLIENT, '--parent', str(os.getpid())], creationflags=cls.FLAGS, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            processes.append(new_worker)
            fresh = cls.call(op='submit', id='after-broker-restart', client=cls.CLIENT, run=spec)['job']['id']
            assert cls.wait(fresh)['state'] == 'succeeded'
            results.append('broker restart expires pending grant, preserves unknown outcome and idempotent id, no replay')
            mcp = subprocess.Popen([str(cls.HOST), 'mcp', '--client', cls.CLIENT + '-mcp'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', creationflags=cls.FLAGS)
            processes.append(mcp)
            for request in [dict(jsonrpc='2.0', id=1, method='initialize', params={}), dict(jsonrpc='2.0', id=2, method='tools/list')]:
                mcp.stdin.write(json.dumps(request)+'\n');mcp.stdin.flush()
                answer = json.loads(mcp.stdout.readline())
                assert answer['id'] == request['id'] and 'result' in answer, answer
                if request['id'] == 1:
                    assert 'instructions' in answer['result'] and 'description' in answer['result']['instructions'] and 'taskName' in answer['result']['instructions']
                if request['id'] == 2:
                    tools = {x['name']: x for x in answer['result']['tools']}
                    assert 'description' in tools['run']['inputSchema']['required'], tools['run']
                    assert 'taskName' in tools['request_access']['inputSchema']['required'], tools['request_access']
            mcp.stdin.write(json.dumps(dict(jsonrpc='2.0', id=3, method='tools/call', params=dict(name='run', arguments=dict(code="'mcp-ok'", cwd=str(cls.ROOT), privilege='normal'))))+'\n');mcp.stdin.flush()
            missing = json.loads(mcp.stdout.readline())
            assert missing['id'] == 3 and missing['result']['isError'], missing
            missing_data = json.loads(missing['result']['content'][0]['text'])
            assert missing_data['error'] == 'description_required', missing_data
            mcp.stdin.write(json.dumps(dict(jsonrpc='2.0', id=4, method='tools/call', params=dict(name='run', arguments=dict(description='通过 MCP 运行标记命令并验证审计字段。', code="'mcp-ok'", cwd=str(cls.ROOT), privilege='normal'))))+'\n');mcp.stdin.flush()
            answer = json.loads(mcp.stdout.readline())
            data = json.loads(answer['result']['content'][0]['text'])
            assert cls.wait(data['job']['id'])['state'] == 'succeeded'
            results.append('real stdio MCP initialize/list/call -> broker -> worker -> PowerShell')
            metadata_client = cls.CLIENT + '-metadata'
            metadata = subprocess.Popen([str(cls.HOST), 'mcp', '--client', metadata_client], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', creationflags=cls.FLAGS)
            processes.append(metadata)
            def exchange(proc, request):
                proc.stdin.write(json.dumps(request, ensure_ascii=False) + '\n'); proc.stdin.flush()
                response = json.loads(proc.stdout.readline())
                assert response['id'] == request['id'] and 'result' in response, response
                return response['result']
            exchange(metadata, dict(jsonrpc='2.0', id='init-title', method='initialize', params=dict(clientInfo=dict(name='Fallback Name', title='Human Console'))))
            call = dict(jsonrpc='2.0', id='run-named', method='tools/call', params=dict(name='run', arguments=dict(description='核验 MCP 客户端显示名称和任务名进入任务记录。', taskName='名称验收任务', code="'metadata-ok'", cwd=str(cls.ROOT), privilege='normal')))
            named = json.loads(exchange(metadata, call)['content'][0]['text'])['job']
            assert named['client'] == metadata_client and named['clientName'] == 'Human Console' and named['taskName'] == '名称验收任务', named
            assert cls.wait(named['id'])['state'] == 'succeeded'
            exchange(metadata, dict(jsonrpc='2.0', id='init-fallback', method='initialize', params=dict(clientInfo=dict(name='Fallback Name', title=''))))
            fallback = json.loads(exchange(metadata, dict(call, id='run-fallback'))['content'][0]['text'])['job']
            assert fallback['client'] == metadata_client and fallback['clientName'] == 'Fallback Name', fallback
            override = subprocess.Popen([str(cls.HOST), 'mcp', '--client', metadata_client + '-override', '--display-name', 'Configured Console'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', creationflags=cls.FLAGS)
            processes.append(override)
            exchange(override, dict(jsonrpc='2.0', id='init-override', method='initialize', params=dict(clientInfo=dict(title='Ignored Protocol Name'))))
            overridden = json.loads(exchange(override, dict(call, id='run-override'))['content'][0]['text'])['job']
            assert overridden['client'] == metadata_client + '-override' and overridden['clientName'] == 'Configured Console', overridden
            grant_result = json.loads(exchange(metadata, dict(jsonrpc='2.0', id='grant-name', method='tools/call', params=dict(name='request_access', arguments=dict(taskName='名称授权保留'))))['content'][0]['text'])
            assert grant_result['grant']['client'] == metadata_client and grant_result['grant']['clientName'] == 'Fallback Name', grant_result
            state = cls.call(op='snapshot')
            stored_grant = next(g for g in state['snapshot']['grants'] if g['id'] == grant_result['grant']['id'])
            assert stored_grant['client'] == metadata_client and stored_grant['clientName'] == 'Fallback Name' and stored_grant['taskName'] == '名称授权保留', stored_grant
            cls.call(op='deny', id=stored_grant['id'])
            for bad_name in cls.INVALID_NAMES:
                bad = cls.call(op='request_access', client=metadata_client, clientName=bad_name, taskName='拒绝无效显示名')
                assert not bad['ok'] and bad['error'] == 'invalid_request', bad
            assert cls.call(op='request_access', client=metadata_client + '-legacy', taskName='旧请求兼容')['ok']
            results.append('MCP title/name fallback and display override persist client/task names without changing authorization key; invalid names rejected and missing legacy field accepted')
            print(json.dumps(dict(passed=len(results), checks=results), ensure_ascii=False))
        finally:
            if mcp and mcp.stdin and not mcp.stdin.closed:
                mcp.stdin.close()
            for proc in reversed(processes):
                if proc.poll() is None:
                    proc.kill()
                try: proc.wait(timeout=10)
                except subprocess.TimeoutExpired: proc.kill(); proc.wait(timeout=10)


Integration.run()
