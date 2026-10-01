import copy
import json
from pathlib import Path
import tempfile
import unittest
from check_config import validate


class SetupTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        (self.root / 'runtime').mkdir()
        for name in ('outbox.jsonl', 'unread.jsonl'):
            (self.root / 'runtime' / name).symlink_to('/dev/null')
        self.config = json.loads((Path(__file__).parent / 'config.example.json').read_text())
        self.config['channel'] = 'fixture-room'

    def tearDown(self):
        self.tmp.cleanup()

    def test_receive_only_config(self):
        validate(self.config, self.root)

    def test_rejects_unsafe_options(self):
        for key, value in [('hook', {}), ('pass', 'fixture'), ('nick', 'other'), ('base', '.'), ('auto_ack', {'enabled': True}), ('channel', 'your-channel-name'), ('url', 'wss://user:fixture@example.test/relay'), ('trip', 'not-a-trip')]:
            with self.subTest(key=key):
                config = copy.deepcopy(self.config)
                config[key] = value
                with self.assertRaises(ValueError):
                    validate(config, self.root)

    def test_rejects_writable_outbox(self):
        outbox = self.root / 'runtime/outbox.jsonl'
        outbox.unlink()
        outbox.write_text('')
        with self.assertRaises(ValueError):
            validate(self.config, self.root)


if __name__ == '__main__':
    unittest.main()
