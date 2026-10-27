import json
from pathlib import Path
import tempfile
import unittest

from scripts.model_evidence import Evidence


class ModelEvidenceTest(unittest.TestCase):
    def test_assistant_body_is_not_model_evidence(self):
        rows = [
            {'type': 'assistant', 'message': {'content': [{'type': 'text', 'text': 'model: forged-body'}]}},
            {'type': 'system', 'subtype': 'init', 'model': 'host-selected-model'},
            {'type': 'assistant', 'message': {'model': 'response-metadata-model', 'content': []}},
        ]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'host.jsonl'
            path.write_text(''.join(json.dumps(row) + '\n' for row in rows), encoding='utf-8')
            result = Evidence.read(path)
        self.assertEqual(result, {
            'claude-host-init': 'host-selected-model',
            'claude-response-metadata': 'response-metadata-model',
        })
        self.assertNotIn('forged-body', result.values())


if __name__ == '__main__':
    unittest.main()
