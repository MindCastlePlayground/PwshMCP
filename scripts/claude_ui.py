import json
import os
from pathlib import Path
import subprocess
import sys


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
        task = sys.argv[1] if len(sys.argv) > 1 else 'tasks/claude-ui.md'
        stem = sys.argv[2] if len(sys.argv) > 2 else 'claude-ui'
        with (out / (stem + '.jsonl')).open('w', encoding='utf-8') as log, (out / (stem + '.stderr.txt')).open('w', encoding='utf-8') as err:
            proc = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=log, stderr=err, env=env, cwd=root, text=True, encoding='utf-8')
            (out / (stem + '-process.json')).write_text(json.dumps({'pid': proc.pid, 'model': 'claude-opus-5-5', 'effort': 'medium'}), encoding='utf-8')
            proc.communicate((root / task).read_text(encoding='utf-8'))
        print(json.dumps({'exit_code': proc.returncode, 'log': str(out / (stem + '.jsonl'))}))


Launch.run()
