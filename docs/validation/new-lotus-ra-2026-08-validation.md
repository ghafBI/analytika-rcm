# New Lotus MC remittance report validation

- Report period: 2026-08-01 to 2026-09-04
- Generated rows: 996 distinct claim references
- Referenced RA files: 330; source files represented in parsed RA cache: 330
- RA received amount: report AED 114,871.80; source AED 114,871.80
- Approved amount: report AED 84,611.24; source AED 84,611.24
- Claim-level amount mismatches at 0.01 tolerance: 0
- Payment-reference presence mismatches: 0
- Edge cases checked: 196 claims spanning multiple RA files and 119 zero-paid claims carrying denial data
- Representative identifiers were SHA-256 truncated for this record: multi-RA `f2d2d2f889a4`; zero-paid denial `33e79515520f`

Method: the exported workbook was compared read-only against `XmlParsedRecords` rows produced from the corresponding RA XML, keyed by facility and claim reference. The validator aggregates all RA rows per claim, matching report generation logic. No live rows were changed.
