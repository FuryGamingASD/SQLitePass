# Independent KeePass producer fixture

`real-keepass-2.62-fixture.kdbx` is an encrypted, synthetic KDBX 4.0 database created by KeePass 2.62 using its KeePassLib writer. The fixture includes one synthetic entry and Unicode text; it contains no real user data and no plaintext KDBX file is stored.

- Official KeePass 2.62 distribution archive SHA-256: `49b008e4ca9e9db9cb22e1173ec0f7676235b5309d7b47213cf81010cd1bbeed`
- Encrypted fixture SHA-256: `ccc350e5bb2d7a48f40e6198c5557650e0767b51d6dcaae131158749ad99fe7f`
- Producer setup script used during fixture creation: `%TEMP%\SQLitePass-KeePass-2.62-QA\create-fixture.ps1`

The importer test decrypts the fixture with a synthetic test password and checks the KDBX version and imported entry fields. The password and synthetic field values are test data, not credentials.
