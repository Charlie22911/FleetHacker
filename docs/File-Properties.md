# Saved file properties

Scans save these properties for each recorded file location:

- Windows file owner account, when its name can be resolved.
- Owner security identifier (SID).
- Filesystem date created and date modified, in UTC.
- The time and path at which the properties were observed.

Ownership identifies the Windows security principal reported for the file. It can be an account or a group. A SID remains available when its account name cannot be resolved. Filesystem creation dates can change when a file is copied. Unavailable properties are identified explicitly; they do not prevent a file from being fingerprinted.

## View and refresh

Open **Details / notes**, select a row under **Recorded locations**, and expand **File properties**. Each duplicate location has its own properties. The section is collapsed initially and scrolls within a limited area when expanded.

Choose **Refresh file properties** to update the selected location. Refresh checks that the source still matches its recorded file identity, size and modified date. A changed file needs a rescan. An offline or inaccessible source leaves the previous saved properties intact. Properties from a previous source location are labeled as an earlier observation after a reconnection.

You can still mark a saved item **Non-compliant** or move it into **Evidence** when its original has moved or been deleted. Batch review saves these historical decisions alongside decisions for available files and reports how many originals were unavailable. Evidence protects a snapshot of the saved details and any available preview with the Supervisor password; metadata-only records are supported. A missing original is never labeled as retained. See [Historical Evidence](Historical-Evidence.md).

Existing databases upgrade with a pre-upgrade backup. Encryption is preserved in that backup. Earlier records have no inferred owner or creation date; refresh their properties or rescan their source folder to fill them. Rescanning preserves supported existing review decisions.

Archive members do not have Windows file ownership or a reliable filesystem creation date. They retain the archive entry's recorded modified date. The container's owner is not substituted for a member's owner.

## Reports and protection

CSV and HTML review reports append saved creation time, owner, SID, ownership-read status, observation time and path, and the observed modified time. Case sheets show the saved properties even when the original source is unavailable, alongside the outcome of any current source check.

Newly retained originals include file properties observed during capture in their authenticated metadata. Supervisor access is required to read that retained metadata or include Evidence in a report. Earlier retained captures keep their original metadata unchanged. The general file-property records follow the database's existing encryption and access controls.

Portable Windows backups preserve the properties. Exporting a database to the deprecated HTML database format is unavailable when file properties are present because that format cannot preserve them. This does not affect HTML review reports.
