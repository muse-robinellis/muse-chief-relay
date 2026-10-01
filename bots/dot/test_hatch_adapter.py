import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from mention_hook import next_event
from hatch_adapter import retry


class HatchTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.root=Path(self.tmp.name)
        self.inbox=self.root/'inbox.jsonl';self.inbox.write_text('');self.db=self.root/'hatch.sqlite'
        self.runtime=self.root/'runtime.sh'
        self.runtime.write_text('''wake() { if [[ "${MOCK_FAIL:-0}" == 1 ]]; then exit 91; fi; python3 -c 'import json,sys;print(json.dumps({"call":"wake","label":sys.argv[1],"payload":json.loads(sys.argv[2])}))' "$1" "$2"; exit 0; }
silent() { python3 -c 'import json,sys;print(json.dumps({"call":"silent","label":sys.argv[1],"payload":json.loads(sys.argv[2])}))' "$1" "$2"; exit 0; }
''')
        self.env=os.environ|{'HATCH_HOOK_RUNTIME':str(self.runtime),'DOT_RELAY_INBOX':str(self.inbox),'DOT_HATCH_DATABASE':str(self.db)}
        self.script=Path(__file__).parent/'hatch-mention-hook.sh'

    def tearDown(self):self.tmp.cleanup()

    def call(self,**extra):
        r=subprocess.run(['bash',str(self.script)],env=self.env|extra,text=True,capture_output=True,timeout=10)
        if r.returncode: return r.returncode
        return json.loads(r.stdout)

    def append(self,ident=1,text='@dot fixture',nick='fixture',direction='in',kind='chat'):
        with self.inbox.open('a') as f:f.write(json.dumps({'dir':direction,'msg':{'type':kind,'id':ident,'nick':nick,'text':text,'trip':None,'ts':1}})+'\n')

    def test_shell_contract_and_payload(self):
        self.append();self.assertEqual(self.call()['call'],'silent')
        self.append(2,'Hello @DoT!');r=self.call()
        self.assertEqual(r['call'],'wake');self.assertEqual(set(r['payload']['messages'][0]),{'nick','trip','text','ts'})
        self.assertEqual(self.call()['call'],'silent')

    def test_dry_run_does_not_write_or_wake(self):
        self.assertEqual(self.call(HATCH_HOOK_DRY_RUN='1')['call'],'silent');self.assertFalse(self.db.exists())
        self.call();self.append();before=self.db.read_bytes()
        r=self.call(HATCH_HOOK_DRY_RUN='1');self.assertTrue(r['payload']['would_wake']);self.assertEqual(r['call'],'silent')
        self.assertEqual(self.db.read_bytes(),before);self.assertEqual(self.call()['call'],'wake')

    def test_malformed_self_outbound_nonmentions(self):
        self.call()
        with self.inbox.open('a') as f:f.write('bad json\n[]\n{"dir":"in","msg":null}\n')
        self.append(1,nick='DOT');self.append(2,direction='out');self.append(3,text='@dotty');self.append(4,kind='presence')
        self.assertEqual(self.call()['call'],'silent')

    def test_replay_rotation_and_partial_records(self):
        self.call();self.append(1);self.assertEqual(self.call()['call'],'wake')
        self.inbox.rename(self.root/'old');self.inbox.write_text('')
        replay={'type':'chat','id':2,'nick':'fixture','text':'@dot replay'}
        self.inbox.write_text(json.dumps({'dir':'in','msg':{'type':'welcome','replay':[replay]}})+'\n'+json.dumps({'dir':'in','msg':replay})+'\n')
        self.append(1);self.assertEqual(self.call()['call'],'silent')
        row=json.dumps({'dir':'in','msg':{'cmd':'chat','id':3,'nick':'fixture','text':'@dot fresh'}})
        with self.inbox.open('a') as f:f.write(row)
        self.assertEqual(self.call()['call'],'silent')
        with self.inbox.open('a') as f:f.write('\n')
        self.assertEqual(self.call()['call'],'wake')

    def test_failed_wake_is_retained_but_not_automatically_retried(self):
        self.call();self.append();self.assertEqual(self.call(MOCK_FAIL='1'),91)
        self.assertEqual(self.call()['call'],'silent')
        event=next_event(self.db);self.assertIsNotNone(event)
        self.assertEqual(retry(self.db,event['event_id']),1)
        self.assertEqual(self.call()['call'],'wake')

    def test_missing_runtime_is_a_real_blocker(self):
        r=subprocess.run(['bash',str(self.script)],env={k:v for k,v in self.env.items() if k!='HATCH_HOOK_RUNTIME'},text=True,capture_output=True)
        self.assertNotEqual(r.returncode,0);self.assertIn('Hatch runtime is required',r.stderr)


if __name__=='__main__':unittest.main()
