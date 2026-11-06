module.exports = (app, replaceOnce) => {
  const start = '  async function createNewWorkspaceImpl(fromStartup = false) {';
  const end = '  function showDatabaseOpening(';
  const first = app.indexOf(start);
  const last = app.indexOf(end, first);
  if (first < 0 || last < 0) throw new Error('Browser database creation markers are missing.');
  let creation = app.slice(first, last);
  const pickerStart = '    if (!window.showSaveFilePicker) throw new Error([';
  const pickerEnd = '    const chooserWasOpen = showDatabaseOpening(';
  const a = creation.indexOf(pickerStart);
  const b = creation.indexOf(pickerEnd);
  if (a < 0 || b < a) throw new Error('Browser file-picker markers are missing.');
  creation = creation.slice(0, a) + [
    '    let handle = null;',
    '    if (typeof window.showSaveFilePicker === "function") {',
    '      try {',
    '        handle = await window.showSaveFilePicker({',
    '          suggestedName: `image-compliance-${new Date().toISOString().slice(0, 10)}.sqlite`,',
    '          types: [{description:"SQLite database",accept:{"application/vnd.sqlite3":[".sqlite",".db"]}}]',
    '        });',
    '      } catch (error) {',
    '        if (error?.name === "AbortError") return false;',
    '        if (error?.name !== "SecurityError" && error?.name !== "NotAllowedError") throw error;',
    '      }',
    '    }',
    '',
  ].join('\n') + creation.slice(b);
  creation = replaceOnce(creation, "showDatabaseOpening(handle.name,",
    "showDatabaseOpening(handle?.name || 'Browser working copy',");
  creation = replaceOnce(creation, 'const initialVersion = await workspaceFileVersion(await handle.getFile());',
    'const initialVersion = handle ? await workspaceFileVersion(await handle.getFile()) : null;');
  creation = replaceOnce(creation, '      workspaceFileVersions.set(handle, initialVersion);',
    '      if (handle) workspaceFileVersions.set(handle, initialVersion);');
  creation = replaceOnce(creation, "$('#database-opening-status').textContent = 'Saving the new database file…';",
    "$('#database-opening-status').textContent = handle ? 'Saving the new database file…' : 'Saving the browser working copy…';");
  creation = replaceOnce(creation, '      await writeWorkspaceHandle(handle, true);',
    '      if (handle) await writeWorkspaceHandle(handle, true);');
  creation = replaceOnce(creation, "      toast('Database created. Automatic scan checkpoints are active.');",
    "      toast(handle ? 'Database created. Automatic scan checkpoints are active.' :\n" +
    "        'Browser database created. Choose Save database to download a portable copy.');");
  app = app.slice(0, first) + creation + app.slice(last);
  return replaceOnce(app,
    "        await checkDatabaseFile(bytes, file.name);\n        await renderWorkspaceState();",
    "        await checkDatabaseFile(bytes, file.name);\n        await renderWorkspaceState();\n" +
    "        if (typeof window.showSaveFilePicker !== 'function') {\n" +
    "          await $('#workspace-dialog').close();\n" +
    "          toast('Database opened in the browser. Choose Save database to download a portable copy.');\n" +
    "          return;\n        }");
};
