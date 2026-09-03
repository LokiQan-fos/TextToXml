Synthetic non-P60 fixtures for Story 1.8 (genericity, format isolation, extended datatypes).

message-only.xml / message-only.txt
  A Descripteur with no header, no footer and no Segment control, plus a three-Ligne input.
  Proves TextToXml names elements from this Descripteur's own Ids, with no P60 tag in the output
  and no change to the library (AC-FR1-9, AC-FR5-13).

typed-values.xml / typed-values-valid.txt / typed-values-invalid.txt
  A Descripteur with decimal (decimalSeparator) and datetime (convert) Champs.
  The valid input normalizes to canonical values; the invalid input yields InvalidDecimal and
  InvalidDate and no XML (CTR-1, CTR-2).

roundtrip.xml / roundtrip.txt
  A Descripteur mixing string, int, decimal and datetime Champs. The normalized XML of the valid
  input deserializes into a record DTO with every value kept and no custom converter (CTR-3).
