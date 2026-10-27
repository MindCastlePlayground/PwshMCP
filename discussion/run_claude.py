import json
import os
from pathlib import Path
import subprocess
import sys


class Discussion:
    @staticmethod
    def run():
        root = Path(__file__).resolve().parent
        req = root / sys.argv[1]
        out = root / sys.argv[2]
        env = dict(os.environ)
        for key in list(env):
            name = key.upper()
            if not name.startswith(('ANTHROPIC_', 'CLAUDE_')) and any(word in name for word in ('PASSWORD', 'TOKEN', 'API_KEY', 'SECRET')):
                env.pop(key)
        args = [r'C:\Users\74756\.local\bin\claude.exe', '-p', '--model', 'claude-opus-5-5', '--effort', 'medium', '--tools', '', '--strict-mcp-config', '--permission-mode', 'plan', '--output-format', 'json']
        if len(sys.argv) > 3:
            args.extend(['--resume', sys.argv[3]])
        result = subprocess.run(args, input=req.read_text(encoding='utf-8'), text=True, encoding='utf-8', capture_output=True, env=env, cwd=root.parent)
        out.write_text(result.stdout, encoding='utf-8')
        (root / (out.stem + '.stderr.txt')).write_text(result.stderr, encoding='utf-8')
        print(json.dumps({'exit_code': result.returncode, 'output': str(out)}, ensure_ascii=False))
        if result.returncode == 0:
            data = json.loads(result.stdout)
            print(json.dumps({'session_id': data.get('session_id'), 'is_error': data.get('is_error'), 'modelUsage': data.get('modelUsage'), 'result': data.get('result')}, ensure_ascii=False))


Discussion.run()
