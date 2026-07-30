# Security policy

Please do not open a public issue for a suspected security vulnerability.
Instead, use GitHub's **Report a vulnerability** feature for this repository.

Security fixes are supported for the latest release. Graph files are treated
as untrusted input. The application bounds native `.graphsketch` input and
collection sizes, rejects malformed JSON safely, and limits ZIP expansion and
XML complexity when opening legacy `.ograph` documents.

Pasted CSV and TSV data is also treated as untrusted input. Import length, row,
column, field, issue, series, and total-point limits are enforced before the
current document is modified.
