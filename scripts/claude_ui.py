import json
import os
from pathlib import Path
import subprocess


class Launch:
    @staticmethod
    def run():
        root = Path(__file__).resolve().parent.parent
        env = dict(os.environ)
        for key in list(env):
            if not key.upper().startswith(('ANTHROPIC_', 'CLAUDE_')) and any(s in key.upper() for s in ('PASSWORD', 'TOKEN', 'API_KEY', 'SECRET')):
                env.pop(key)
        args = [r'C:\Users\74756\.local\bin\claude.exe', '-p', '--model', 'claude-opus-5-5', '--effort', 'medium', '--strict-mcp-config', '--permission-mode', 'acceptEdits', '--tools', 'Read,Write,Edit,Glob,Grep,Bash', '--allowedTools', 'Read', 'Write', 'Edit', 'Glob', 'Grep', 'Bash(dotnet *)', 'Bash(node *)', '--output-format', 'stream-json', '--verbose']
        out = root / 'artifacts'
        out.mkdir(exist_ok=True)
        with (out / 'claude-ui.jsonl').open('w', encoding='utf-8') as log, (out / 'claude-ui.stderr.txt').open('w', encoding='utf-8') as err:
            proc = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=log, stderr=err, env=env, cwd=root, text=True, encoding='utf-8')
            (out / 'claude-ui-process.json').write_text(json.dumps({'pid': proc.pid, 'model': 'claude-opus-5-5', 'effort': 'medium'}), encoding='utf-8')
            proc.communicate((root / 'tasks/claude-ui.md').read_text(encoding='utf-8'))
        print(json.dumps({'exit_code': proc.returncode, 'log': str(out / 'claude-ui.jsonl')}))


Launch.run()
