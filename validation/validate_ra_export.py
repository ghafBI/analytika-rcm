import argparse, hashlib, json, sqlite3
from collections import defaultdict
from decimal import Decimal
from xml.etree import ElementTree as ET
import openpyxl

def child(node, name):
    for item in list(node):
        if item.tag.rsplit('}', 1)[-1] == name:
            return (item.text or '').strip()
    return ''

def money(value):
    try: return Decimal(value or '0')
    except Exception: return Decimal('0')

p = argparse.ArgumentParser()
p.add_argument('--workbook', required=True)
p.add_argument('--database', required=True)
a = p.parse_args()

sheet = openpyxl.load_workbook(a.workbook, read_only=True, data_only=True).active
report = {}
files = set()
for row in sheet.iter_rows(min_row=9, values_only=True):
    if row[0] != 'New Lotus MC' or not row[1]: continue
    report[str(row[1]).strip()] = {'received': money(row[19]), 'paid': money(row[21]), 'payref': str(row[29] or '').strip()}
    for name in str(row[33] or '').split(' | '):
        if name.strip(): files.add(name.strip())

db = sqlite3.connect(f'file:{a.database}?mode=ro', uri=True, timeout=30)
source = defaultdict(lambda: {'received': Decimal('0'), 'paid': Decimal('0'), 'payrefs': set(), 'files': set(), 'denials': set()})
parse_errors = 0
claim_ids = sorted(report)
source_rows = []
for offset in range(0, len(claim_ids), 300):
    part = claim_ids[offset:offset+300]
    marks = ','.join('?' for _ in part)
    source_rows.extend(db.execute(
        f'''SELECT ClaimId, NetAmount, PaidAmount, PaymentReference, FileName, DenialCodesJson
            FROM XmlParsedRecords INDEXED BY IX_XmlParsedRecords_Fac_Claim_NC
            WHERE FacilityId=10 AND ClaimId IN ({marks}) AND RecordKind='Remittance' AND ReadyForReport=1''', part))
for claim_id, received, paid, payref, filename, denials in source_rows:
    rec = source[claim_id]
    rec['received'] += money(received); rec['paid'] += money(paid)
    if payref: rec['payrefs'].add(payref)
    if filename: rec['files'].add(filename)
    if denials and denials != '[]': rec['denials'].add(denials)

cents = lambda value: value.quantize(Decimal('0.01'))
amount_mismatch = [k for k, v in report.items() if k not in source or cents(v['received']) != cents(source[k]['received']) or cents(v['paid']) != cents(source[k]['paid'])]
payment_mismatch = [k for k, v in report.items() if k in source and bool(v['payref']) != bool(source[k]['payrefs'])]
multi_ra = [k for k, v in source.items() if len(v['files']) > 1]
zero_paid_denial = [k for k, v in source.items() if v['paid'] == 0 and v['denials']]
digest = lambda value: hashlib.sha256(value.encode()).hexdigest()[:12]
print(json.dumps({
  'report_rows': len(report), 'referenced_ra_files': len(files), 'source_files_found': len(set(x[4] for x in source_rows if x[4])),
  'source_parse_errors': parse_errors, 'source_claims_matched': len(source),
  'report_received_total': str(cents(sum(x['received'] for x in report.values()))),
  'source_received_total': str(cents(sum(x['received'] for x in source.values()))),
  'report_paid_total': str(cents(sum(x['paid'] for x in report.values()))),
  'source_paid_total': str(cents(sum(x['paid'] for x in source.values()))),
  'amount_mismatch_count': len(amount_mismatch), 'payment_presence_mismatch_count': len(payment_mismatch),
  'multi_ra_claim_count': len(multi_ra), 'zero_paid_with_denial_count': len(zero_paid_denial),
  'representative_multi_ra_hash': digest(multi_ra[0]) if multi_ra else None,
  'representative_zero_paid_denial_hash': digest(zero_paid_denial[0]) if zero_paid_denial else None
}, indent=2))
