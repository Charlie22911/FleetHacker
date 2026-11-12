# Database and Supervisor protection

## Getting started

Choose **Create database** on the welcome screen and save the database on a local drive. Set and confirm two different passwords, each at least 12 characters long:

- The **database password** opens the review workspace and its saved previews, notes, source records and decisions.
- The **Supervisor password** unlocks retained originals, including Evidence, and authenticated capture details. It also authorizes protected exports and Evidence reclassification.

A database is required to continue. Open an existing database instead of creating another one when resuming work. **Exit** closes the application. Cancelling password setup returns to the welcome screen.

Supervisor access starts locked whenever a database opens. Opening **Evidence** asks for the Supervisor password. Choose **Lock Supervisor access** to close the protected view and leave Evidence. Keep passwords in a suitable password manager or other approved location; there is no password recovery.

## Existing databases and backups

Existing unencrypted databases remain readable and show **Not encrypted** beside their filename. To create a protected copy, choose **Maintenance > Save encrypted database copy**, select a new filename and set the database password. The original is preserved. If a Supervisor vault already exists, the copy keeps that vault and password. Otherwise, setup also requires a Supervisor password.

Portable backups and automatic pre-upgrade backups of an encrypted database remain encrypted with its database password. Copies made before encryption retain their earlier protection. Changing a Supervisor password does not change earlier copies. Open encrypted databases with the Windows application; the deprecated HTML edition cannot decrypt them.

## Protection boundaries

The database uses authenticated ChaCha20-Poly1305 page encryption through SQLite3 Multiple Ciphers 2.6.1, with PBKDF2-SHA256 and 600,000 iterations. Retained originals and their capture metadata have a separate AES-256-GCM encryption layer under the Supervisor password. The database password cannot unlock that layer.

Database encryption covers SQLite database and WAL page contents. SQLite temporary working storage stays in memory. Database passwords are protected in process memory during an open session and cleared after its workers close. Database copies are written to an encrypted destination without a decrypted database staging file.

The Supervisor password protects the retained capture. General source records, earlier review history, and ordinary saved previews belong to the database protection layer. The application restricts Evidence browsing and details, but a person who knows the database password can inspect general database records with compatible tools. Moving an item into Evidence cannot revoke an earlier copy or access to the original media in its source folder.

Exported originals, CSV reports, HTML reports and case sheets are deliberate readable outputs. Their destination needs suitable protection. Opening a protected view also displays decrypted content in memory and on screen. Database encryption does not protect an already unlocked Windows session from someone who can use or inspect that session.

## Command-line use

`--create-database PATH` creates an encrypted database and requests a database password followed by a different Supervisor password. An encrypted `--database PATH` scan requests the database password. An attached terminal reads passwords without echoing them. For automation, standard input accepts one UTF-8 password per line. Supply secrets through a protected input channel; do not put them in process arguments or retain them in shell history.

## Building

The native build pins the SQLite3 Multiple Ciphers 2.6.1 source archive by SHA-256 and applies its upstream integration script to the pinned SQLite 3.54.1 amalgamation. The extension is statically linked into the portable executable; it does not require a separately installed database runtime. Its dependency notice is available in **About** and `native/COPYING.sqlite3mc`.
