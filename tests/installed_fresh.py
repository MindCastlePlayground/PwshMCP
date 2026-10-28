import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time
import uuid
import winreg


class Wire:
    JSONRPC = 'jsonrpc'
    JSONRPC_VALUE = '2.0'
    ID = 'id'
    METHOD = 'method'
    PARAMS = 'params'
    NAME = 'name'
    ARGUMENTS = 'arguments'
    CONTENT = 'content'
    TEXT = 'text'
    RESULT = 'result'
    ERROR = 'error'
    IS_ERROR = 'isError'
    TOOLS = 'tools'
    PROTOCOL_VERSION = 'protocolVersion'
    CURSOR = 'cursor'
    JOB = 'job'
    STATE = 'state'
    TASK_ID = 'id'
    OUTPUT = 'output'
    RESULT_DATA = 'result'
    ENVIRONMENT_ID = 'environmentId'
    EXIT_CODE = 'exitCode'
    DESCRIPTION = 'description'
    TASK_NAME = 'taskName'
    CODE = 'code'
    CWD = 'cwd'
    PRIVILEGE = 'privilege'
    REASON = 'reason'
    MARKER = 'marker'
    PID = 'pid'
    LEAKED = 'leaked'
    ADMIN = 'admin'


class Names:
    TOOL_RUN = 'run'
    TOOL_STATUS = 'status'
    TOOL_OUTPUT = 'output'
    METHOD_INIT = 'initialize'
    METHOD_LIST = 'tools/list'
    METHOD_CALL = 'tools/call'
    CLIENT = 'codex'
    ENV_KEY = 'PwshMcpFresh_' + uuid.uuid4().hex
    LOCAL_KEY = 'PwshMcpLocal_' + uuid.uuid4().hex
    REGISTRY = r'Environment'
    CLIENT_INFO = 'clientInfo'
    CLIENT_NAME = 'name'
    PRIV_NORMAL = 'normal'
    PRIV_ELEVATED = 'elevated'
    STATE_DONE = ('succeeded', 'failed', 'cancelled', 'rejected', 'expired', 'outcomeUnknown')
    FLAGS = subprocess.CREATE_NO_WINDOW
    READ_TIMEOUT = 5
    JOB_TIMEOUT = 180


class InstalledFresh:
    def __init__(self, host, elevated, client):
        self.host = Path(host).resolve(strict=True)
        if self.host.name.lower() != 'pwshmcp.host.exe':
            raise ValueError('--host must point to the exact PwshMcp.Host.exe')
        self.elevated = elevated
        self.client = client
        self.proc = None
        self.lines = queue.Queue()
        self.counter = 0
        self.root = winreg.HKEY_CURRENT_USER
        self.key_path = Names.REGISTRY
        self.saved = None

    @staticmethod
    def ps(value):
        return "'" + value.replace("'", "''") + "'"

    @classmethod
    def command(cls, marker, local=False, expected=None):
        env = Names.ENV_KEY
        local_key = Names.LOCAL_KEY
        assignment = f"$env:{local_key}='shell-only';" if local else ''
        expected_check = "if($null -ne $v){throw 'registry value should be absent'}" if expected == '__ABSENT__' else f"if($v -cne {cls.ps(expected)}){{throw 'registry value mismatch'}}"
        source = (
            f"$v=[Environment]::GetEnvironmentVariable({cls.ps(env)},'Process');"
            f"{expected_check}"
            f"$a=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);"
            f"{assignment}"
            f"$o=@{{pid=$PID;marker={cls.ps(marker)};leaked=$env:{local_key};admin=$a}};"
            "ConvertTo-Json -Compress -InputObject $o"
        )
        return source

    def save_registry(self):
        with winreg.OpenKey(self.root, self.key_path, 0, winreg.KEY_QUERY_VALUE | winreg.KEY_SET_VALUE) as key:
            try:
                value, kind = winreg.QueryValueEx(key, Names.ENV_KEY)
                self.saved = (True, value, kind)
            except FileNotFoundError:
                self.saved = (False, None, None)

    def set_registry(self, value):
        with winreg.OpenKey(self.root, self.key_path, 0, winreg.KEY_SET_VALUE) as key:
            if value is None:
                try:
                    winreg.DeleteValue(key, Names.ENV_KEY)
                except FileNotFoundError:
                    pass
            else:
                winreg.SetValueEx(key, Names.ENV_KEY, 0, winreg.REG_SZ, value)

    def restore_registry(self):
        if self.saved is None:
            return
        present, value, kind = self.saved
        with winreg.OpenKey(self.root, self.key_path, 0, winreg.KEY_SET_VALUE) as key:
            if present:
                winreg.SetValueEx(key, Names.ENV_KEY, 0, kind, value)
            else:
                try:
                    winreg.DeleteValue(key, Names.ENV_KEY)
                except FileNotFoundError:
                    pass

    def start(self):
        self.proc = subprocess.Popen(
            [str(self.host), 'mcp', '--client', self.client], stdin=subprocess.PIPE,
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, encoding='utf-8',
            bufsize=1, creationflags=Names.FLAGS,
        )
        threading.Thread(target=self.read_lines, daemon=True).start()
        self.exchange(Names.METHOD_INIT, {Wire.PROTOCOL_VERSION: '2025-06-18', Names.CLIENT_INFO: {Names.CLIENT_NAME: self.client}})
        listing = self.exchange(Names.METHOD_LIST, {})
        tools = {tool[Wire.NAME]: tool for tool in listing[Wire.TOOLS]}
        if Names.TOOL_RUN not in tools or Names.TOOL_STATUS not in tools or Names.TOOL_OUTPUT not in tools:
            raise AssertionError('required MCP tools missing')

    def read_lines(self):
        try:
            for line in self.proc.stdout:
                self.lines.put(line)
        finally:
            self.lines.put(None)

    def exchange(self, method, params):
        self.counter += 1
        request_id = self.counter
        message = {Wire.JSONRPC: Wire.JSONRPC_VALUE, Wire.ID: request_id, Wire.METHOD: method}
        if params:
            message[Wire.PARAMS] = params
        self.proc.stdin.write(json.dumps(message, ensure_ascii=False) + '\n')
        self.proc.stdin.flush()
        try:
            line = self.lines.get(timeout=Names.READ_TIMEOUT)
        except queue.Empty as exc:
            raise TimeoutError('MCP stdio response deadline exceeded') from exc
        if line is None:
            raise RuntimeError('MCP process closed stdout')
        response = json.loads(line)
        if response.get(Wire.ID) != request_id or Wire.ERROR in response:
            raise RuntimeError('MCP JSON-RPC error')
        return response[Wire.RESULT]

    def call(self, name, arguments):
        result = self.exchange(Names.METHOD_CALL, {Wire.NAME: name, Wire.ARGUMENTS: arguments})
        if result.get(Wire.IS_ERROR):
            raise RuntimeError('MCP tool call failed')
        return json.loads(result[Wire.CONTENT][0][Wire.TEXT])

    def status(self, task_id):
        return self.call(Names.TOOL_STATUS, {Wire.TASK_ID: task_id})

    def cancel(self, task_id):
        try:
            self.call('cancel', {Wire.TASK_ID: task_id})
        except Exception:
            pass

    def run_one(self, index, value, local=False, expected=None):
        marker = uuid.uuid4().hex
        arguments = {
            Wire.DESCRIPTION: f'Installed fresh environment acceptance command {index}: verify registry refresh and process isolation.',
            Wire.TASK_NAME: f'Installed fresh environment {index}',
            Wire.CODE: self.command(marker, local, expected),
            Wire.CWD: str(self.host.parent),
            Wire.PRIVILEGE: Names.PRIV_ELEVATED if self.elevated else Names.PRIV_NORMAL,
        }
        submitted = self.call(Names.TOOL_RUN, arguments)
        job = submitted.get(Wire.JOB)
        if not job:
            raise AssertionError('run did not return a job')
        task_id = job[Wire.TASK_ID]
        deadline = time.monotonic() + Names.JOB_TIMEOUT
        announced = False
        try:
            while time.monotonic() < deadline:
                state_response = self.status(task_id)
                job = state_response.get(Wire.JOB, {})
                state = job.get(Wire.STATE)
                if state in Names.STATE_DONE:
                    break
                if state == 'needsApproval' and not announced:
                    print('awaiting tray approval', flush=True)
                    announced = True
                time.sleep(.5)
            else:
                self.cancel(task_id)
                raise TimeoutError('command did not finish within 180 seconds')
            if state != 'succeeded':
                raise AssertionError(f'command ended in state {state}')
            result = job.get(Wire.RESULT_DATA) or {}
            output_response = self.call(Names.TOOL_OUTPUT, {Wire.TASK_ID: task_id, Wire.CURSOR: 0})
            output = output_response.get(Wire.OUTPUT, '')
            payload = json.loads(output.strip().splitlines()[-1])
            if payload.get(Wire.MARKER) != marker or payload.get(Wire.PID) is None:
                raise AssertionError('command marker or PID missing')
            if payload.get(Wire.ADMIN) is not self.elevated:
                raise AssertionError('command privilege does not match --elevated')
            if local and payload.get(Wire.LEAKED) != 'shell-only':
                raise AssertionError('first command did not see shell-local variable')
            if not local and payload.get(Wire.LEAKED) is not None:
                raise AssertionError('shell-local variable leaked into a fresh command')
            if result.get(Wire.EXIT_CODE) != 0 or not result.get(Wire.ENVIRONMENT_ID):
                raise AssertionError('execution result missing exit code or environmentId')
            return payload, result
        except Exception:
            self.cancel(task_id)
            raise

    def run(self):
        self.save_registry()
        try:
            self.set_registry('first')
            self.start()
            first = self.run_one(1, 'first', local=True, expected='first')
            self.set_registry('second')
            second = self.run_one(2, 'second', expected='second')
            self.set_registry(None)
            third = self.run_one(3, 'deleted', expected='__ABSENT__')
            pids = {item[0][Wire.PID] for item in (first, second, third)}
            environments = {item[1][Wire.ENVIRONMENT_ID] for item in (first, second, third)}
            if len(pids) != 3 or len(environments) != 3:
                raise AssertionError('command processes/environments were reused')
            print(json.dumps({'passed': True, 'commands': 3, 'distinctPids': 3, 'distinctEnvironments': 3}))
        finally:
            self.restore_registry()
            if self.proc and self.proc.poll() is None:
                self.proc.stdin.close()
                try:
                    self.proc.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    self.proc.kill()
                    self.proc.wait(timeout=3)


class Main:
    @staticmethod
    def run():
        parser = argparse.ArgumentParser()
        parser.add_argument('--host', required=True)
        parser.add_argument('--elevated', action='store_true')
        parser.add_argument('--client', default=Names.CLIENT)
        args = parser.parse_args()
        InstalledFresh(args.host, args.elevated, args.client).run()


if __name__ == '__main__':
    Main.run()
