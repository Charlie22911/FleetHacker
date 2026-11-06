import {FacadeVFS} from '../vendor/wa-sqlite/src/FacadeVFS.js';
import * as VFS from '../vendor/wa-sqlite/src/sqlite-constants.js';

// SQLite reads immutable backup pages directly from the selected File.
class WindowsBackupVFS extends FacadeVFS {
  selected = new Map();
  opened = new Map();

  jOpen(path, id, flags, out) {
    const file = this.selected.get(path);
    if (!file || !(flags & VFS.SQLITE_OPEN_MAIN_DB) || !(flags & VFS.SQLITE_OPEN_READONLY)) {
      return VFS.SQLITE_CANTOPEN;
    }
    this.opened.set(id, file);
    out.setInt32(0, flags, true);
    return VFS.SQLITE_OK;
  }

  jClose(id) {
    this.opened.delete(id);
    return VFS.SQLITE_OK;
  }

  jAccess(path, flags, out) {
    out.setInt32(0, 0, true);
    return VFS.SQLITE_OK;
  }

  jDeviceCharacteristics() {
    return VFS.SQLITE_IOCAP_IMMUTABLE;
  }

  jFileSize(id, out) {
    out.setBigInt64(0, BigInt(this.opened.get(id).size), true);
    return VFS.SQLITE_OK;
  }

  async jRead(id, target, offset) {
    try {
      const file = this.opened.get(id);
      const bytes = new Uint8Array(await file.slice(offset, offset + target.length).arrayBuffer());
      target.set(bytes);
      if (bytes.length === target.length) return VFS.SQLITE_OK;
      target.fill(0, bytes.length);
      return VFS.SQLITE_IOERR_SHORT_READ;
    } catch {
      return VFS.SQLITE_IOERR_READ;
    }
  }
}

let windowsBackupVfs;

async function openWindowsBackup(file) {
  if (!(file instanceof Blob) || file.size < 100) throw new Error('Choose a Windows portable backup.');
  const header = new DataView(await file.slice(0, 100).arrayBuffer());
  const magic = new TextDecoder().decode(new Uint8Array(header.buffer, 0, 16));
  if (magic !== 'SQLite format 3\0' || header.getUint32(68) !== windowsCatalogApplicationId) {
    throw new Error('Choose a backup from the Windows reviewer. Open browser catalogs from the main reviewer.');
  }
  if (header.getUint8(18) !== 1 || header.getUint8(19) !== 1) {
    throw new Error('This catalog uses WAL. In the Windows reviewer, use Maintenance → Portable backup and select the new file.');
  }
  if (header.getUint32(60) !== windowsCatalogSchema) {
    throw new Error('This viewer supports Windows schema ' + windowsCatalogSchema + '. Open the catalog in the current Windows reviewer and create a new portable backup.');
  }
  const pageSize = header.getUint16(16) === 1 ? 65536 : header.getUint16(16);
  if (pageSize < 512 || pageSize > 65536 || (pageSize & (pageSize - 1)) ||
      file.size % pageSize !== 0 || header.getUint32(28) !== file.size / pageSize) {
    throw new Error('The backup size does not match its SQLite header.');
  }

  if (!windowsBackupVfs) {
    windowsBackupVfs = new WindowsBackupVFS('windows-backup', engine);
    await windowsBackupVfs.isReady();
    sqlite.vfs_register(windowsBackupVfs, false);
  }
  const id = crypto.randomUUID();
  // Reserve room for SQLite's journal suffix in the VFS pathname limit.
  const path = '/win-' + id + '.sqlite';
  let db;
  windowsBackupVfs.selected.set(path, file);
  try {
    const pointer = await sqlite.open_v2(path, VFS.SQLITE_OPEN_READONLY, 'windows-backup');
    db = new Database(id, path, pointer);
    db.windowsBackup = true;
    await db.run('PRAGMA trusted_schema=OFF; PRAGMA query_only=ON; PRAGMA cache_size=-4096;');
    databases.set(id, db);
    return {id};
  } catch (error) {
    if (db) await db.close();
    throw error;
  } finally {
    windowsBackupVfs.selected.delete(path);
  }
}
