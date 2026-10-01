import json
from pathlib import Path
import tempfile
import unittest
from mention_hook import classify, poll, next_event
from reply import queue_reply


class ParticipationTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
        runtime=self.root/'runtime';runtime.mkdir();(runtime/'outbox.jsonl').write_text('');(runtime/'unread.jsonl').symlink_to('/dev/null')
        self.config=self.root/'config.json'
        cfg=json.loads((Path(__file__).parent/'config.example.json').read_text())
        cfg.update(channel='fixture-room',dot_mode='participate',approved_recipients=['Fixture','Other'])
        self.config.write_text(json.dumps(cfg))

    def tearDown(self):self.tmp.cleanup()

    def test_questions_and_other_addressees(self):
        for text in ['Does anyone know why this failed?','What should we try next','Any ideas?']:
            self.assertEqual(classify(text,['Fixture','Other']),'open_question_candidate')
        for text in ['@Other can you check?','Other, can you check?','Hey Other what happened?','Could you check, Other?','This is working now']:
            self.assertIsNone(classify(text,['Fixture','Other']))
        self.assertEqual(classify('@dot can you check with @Other?',['Other']),'addressed_to_dot')

    def test_unknown_sender_does_not_queue(self):
        inbox=self.root/'runtime/inbox.jsonl';inbox.write_text('');db=self.root/'runtime/mentions.sqlite';poll(inbox,db,['Fixture'])
        inbox.write_text(json.dumps({'dir':'in','msg':{'id':1,'type':'chat','nick':'Unknown','text':'Can anyone help?'}})+'\n')
        self.assertEqual(poll(inbox,db,['Fixture'])['queued'],0)

    def test_preview_then_local_queue_then_duplicate_guard(self):
        text='@Fixture Here is a contextual answer.\nSecond line.'
        self.assertEqual(queue_reply(self.config,'Fixture',text,'fixture-event')['status'],'preview_only')
        self.assertEqual((self.root/'runtime/outbox.jsonl').read_text(),'')
        self.assertEqual(queue_reply(self.config,'Fixture',text,'fixture-event',True)['status'],'queued_not_yet_confirmed_sent')
        row=json.loads((self.root/'runtime/outbox.jsonl').read_text());self.assertEqual(row,{'cmd':'chat','text':text})
        with self.assertRaises(ValueError):queue_reply(self.config,'Fixture',text,'fixture-event',True)

    def test_unapproved_and_receiver_only_refused(self):
        with self.assertRaises(ValueError):queue_reply(self.config,'Unknown','hello','event',True)
        cfg=json.loads(self.config.read_text());cfg['dot_mode']='receive-only';self.config.write_text(json.dumps(cfg))
        with self.assertRaises(ValueError):queue_reply(self.config,'Fixture','hello','event',True)
        cfg.update(dot_mode='participate',approved_recipients='Fixture');self.config.write_text(json.dumps(cfg))
        with self.assertRaises(ValueError):queue_reply(self.config,'Fixture','hello','event',True)


if __name__=='__main__':unittest.main()
