"""Read host-written model metadata, never model-generated message text."""
import argparse
import json
from pathlib import Path


class Key:
    TYPE = 'type'
    SUB = 'subtype'
    PAYLOAD = 'payload'
    MODEL = 'model'
    MESSAGE = 'message'
    USAGE = 'modelUsage'
    SYSTEM = 'system'
    INIT = 'init'
    ASSISTANT = 'assistant'
    TURN = 'turn_context'
    RESULT = 'result'


class Evidence:
    @staticmethod
    def read(path):
        found = {}
        with Path(path).open(encoding='utf-8-sig') as stream:
            for line in stream:
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    continue  # Host may still be appending the final record.
                kind = row.get(Key.TYPE)
                value = None
                source = None
                if kind == Key.SYSTEM and row.get(Key.SUB) == Key.INIT:
                    value, source = row.get(Key.MODEL), 'claude-host-init'
                elif kind == Key.ASSISTANT:
                    value, source = row.get(Key.MESSAGE, {}).get(Key.MODEL), 'claude-response-metadata'
                elif kind == Key.TURN:
                    value, source = row.get(Key.PAYLOAD, {}).get(Key.MODEL), 'codex-turn-context'
                elif kind == Key.RESULT:
                    for model in row.get(Key.USAGE, {}):
                        found['claude-usage:' + model] = model
                if isinstance(value, str) and value and source:
                    found[source] = value
        return found

    @staticmethod
    def main():
        parser = argparse.ArgumentParser(description=__doc__)
        parser.add_argument('path', type=Path)
        args = parser.parse_args()
        print(json.dumps(Evidence.read(args.path), ensure_ascii=False))


if __name__ == '__main__':
    Evidence.main()
