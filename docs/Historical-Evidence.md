# Evidence when the original is unavailable

Evidence can retain a saved record after its source file is moved, deleted, changed or becomes inaccessible. Select the item, or choose **Select page** for a batch, and move it into **Evidence**. Enter the Supervisor password if prompted.

When a matching original is available, FleetHacker retains and verifies its bytes. Otherwise it saves an encrypted historical snapshot containing the available preview, recorded fingerprint and size, saved locations, ownership and dates, review notes and steganography notes. A record without a preview or source location can still be retained. The original source records and review history remain in the database.

Historical Evidence appears as **Original unavailable**. Open its saved details to view the protected snapshot. It is authenticated as a saved historical record; its missing original bytes have not been verified or retained. **Export original** is unavailable for these records.

The Supervisor password protects historical snapshots and their previews independently of the database password. Locking Supervisor access closes the protected view. Reclassification out of Evidence requires authenticating the saved record and preserves its encrypted snapshot.

## Exports and backups

**Export selected protected Evidence package** includes historical records alongside retained originals. The package preserves their distinction, saved details and available previews. Open it with the current Windows edition and the Supervisor password used when it was exported. A historical record cannot produce an original file from the package.

CSV and HTML review reports can include authenticated historical Evidence after Supervisor unlock and identify it as **Historical Evidence (original unavailable)**. These deliberately exported reports are plaintext.

Portable database backups preserve all saved records and encrypted historical snapshots. The protected detail view and package snapshot have bounded text and location lists; a notice identifies larger records whose additional locations or extended text remain in the database. Use a portable database backup to retain the complete workspace.

For an ordinary Non-compliant item, reconnect its source and choose **Retain originals** to add a verified original later without changing its saved review. An existing verified retained original is used even when its source file is subsequently deleted.
