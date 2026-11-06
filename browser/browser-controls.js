(() => {
  const byId = id => document.getElementById(id);
  const inspector = byId('inspect-dialog');
  const full = byId('inspect-fullscreen');
  const video = byId('inspect-video');
  const rate = byId('inspect-video-rate');
  full.disabled = !document.fullscreenEnabled;
  full.addEventListener('click', async () => {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await document.documentElement.requestFullscreen();
    } catch {
      full.textContent = 'Full screen unavailable';
    }
  });
  document.addEventListener('fullscreenchange', () => {
    const active = document.fullscreenElement === document.documentElement;
    inspector.classList.toggle('browser-fullscreen', active);
    full.textContent = active ? 'Exit full screen' : 'Full screen';
  });
  inspector.addEventListener('close', () => {
    if (!inspector.open && inspector.classList.contains('browser-fullscreen')) {
      document.exitFullscreen().catch(() => {});
    }
  });
  const applyRate = () => { video.playbackRate = Number(rate.value); };
  rate.addEventListener('change', applyRate);
  video.addEventListener('loadedmetadata', applyRate);

  const dialog = byId('windows-catalog-dialog');
  const picker = byId('windows-backup-file');
  const status = byId('windows-backup-status');
  const result = byId('windows-backup-results');
  const cards = byId('windows-backup-cards');
  const bucket = byId('windows-backup-bucket');
  const prev = byId('windows-backup-prev');
  const next = byId('windows-backup-next');
  const pageLabel = byId('windows-backup-page');
  const pageSize = 20;
  const urls = new Set();
  const safeMime = new Set(['image/jpeg', 'image/png', 'image/webp']);
  let database, page = 0, total = 0, generation = 0;

  const rows = async (db, query, parameters) => (await db.exec(query, parameters))[0]?.values || [];
  const clear = () => {
    cards.replaceChildren();
    for (const url of urls) URL.revokeObjectURL(url);
    urls.clear();
  };
  const paragraph = text => {
    const p = document.createElement('p');
    p.textContent = text;
    return p;
  };
  function card(row, db, current) {
    const [id, name, extension, size, reviewer, notes, mime, bytes] = row;
    const item = document.createElement('li');
    if (bytes && safeMime.has(mime)) {
      const img = document.createElement('img');
      img.alt = 'Saved preview of ' + name;
      const url = URL.createObjectURL(new Blob([bytes], {type: mime}));
      urls.add(url);
      img.src = url;
      item.append(img);
    } else {
      item.append(paragraph('Preview unavailable'));
    }
    const description = document.createElement('div');
    const title = document.createElement('strong');
    title.textContent = name;
    description.append(title, paragraph([extension?.toUpperCase(), Number(size).toLocaleString() + ' bytes'].filter(Boolean).join(' · ')));
    if (reviewer) description.append(paragraph('Reviewer · ' + reviewer));
    if (notes) description.append(paragraph(notes));
    const details = document.createElement('details');
    const summary = document.createElement('summary');
    summary.textContent = 'Recorded locations';
    details.append(summary);
    details.addEventListener('toggle', async () => {
      if (!details.open || details.dataset.loaded) return;
      details.dataset.loaded = 'true';
      const loading = paragraph('Loading locations…');
      details.append(loading);
      try {
        const locations = await rows(db, `SELECT r.path,o.path,o.superseded
          FROM group_occurrences o JOIN source_roots r ON r.id=o.root_id
          WHERE o.group_id=? ORDER BY o.superseded,o.occurrence_order LIMIT 101`, [id]);
        if (generation !== current) return;
        loading.remove();
        for (const [root, path, historical] of locations.slice(0, 100)) {
          details.append(paragraph(root.replace(/[\\/]+$/, '') + '\\' + path + (historical ? ' · Historical' : '')));
        }
        if (!locations.length) details.append(paragraph('No recorded locations.'));
        if (locations.length > 100) details.append(paragraph('Showing the first 100 locations.'));
      } catch (error) {
        loading.textContent = error.message;
      }
    });
    description.append(details);
    item.append(description);
    return item;
  }

  async function render() {
    const db = database;
    if (!db) return;
    const current = ++generation;
    clear();
    prev.disabled = next.disabled = true;
    pageLabel.textContent = '';
    status.textContent = 'Reading backup…';
    try {
      const selected = bucket.value;
      const [[count]] = await rows(db, 'SELECT count(*) FROM review_groups WHERE status=? AND retired=0', [selected]);
      if (generation !== current) return;
      total = Number(count);
      const values = await rows(db, `WITH page AS (
          SELECT id,content_id,display_sequence,reviewer,notes FROM review_groups
          WHERE status=? AND retired=0 ORDER BY display_sequence LIMIT ? OFFSET ?
        )
        SELECT g.id, COALESCE(o.name,'Item '||g.id), o.extension, c.size,g.reviewer,g.notes,p.mime,
          CASE WHEN p.state='available' AND p.mime IN ('image/jpeg','image/png','image/webp')
            AND length(p.bytes)<=32768 THEN p.bytes END
        FROM page g JOIN contents c ON c.id=g.content_id
        LEFT JOIN group_occurrences o ON o.group_id=g.id AND o.id=(
          SELECT q.id FROM group_occurrences q WHERE q.group_id=g.id
          ORDER BY q.superseded,q.modified_ticks DESC,q.occurrence_order LIMIT 1
        )
        LEFT JOIN previews p ON p.content_id=g.content_id ORDER BY g.display_sequence`, [selected, pageSize, page * pageSize]);
      if (generation !== current) return;
      cards.append(...values.map(row => card(row, db, current)));
      pageLabel.textContent = total ? `${page * pageSize + 1}–${page * pageSize + values.length} of ${total.toLocaleString()}` : 'No items';
      prev.disabled = page === 0;
      next.disabled = (page + 1) * pageSize >= total;
      status.textContent = 'Read-only · ' + picker.files[0]?.name;
    } catch (error) {
      if (generation === current) status.textContent = error.message;
    }
  }

  function release() {
    const db = database;
    database = null;
    result.hidden = true;
    clear();
    return db?.close();
  }
  picker.addEventListener('change', async () => {
    const file = picker.files[0];
    const current = ++generation;
    status.textContent = 'Checking backup…';
    try {
      await release();
      if (!file || generation !== current) return;
      const db = await MediaDatabase.Database.open({windowsBackup: file});
      if (generation !== current) {
        await db.close();
        return;
      }
      database = db;
      page = 0;
      bucket.selectedIndex = 0;
      result.hidden = false;
      await render();
    } catch (error) {
      if (generation === current) status.textContent = error.message;
    }
  });
  bucket.addEventListener('change', () => { page = 0; void render(); });
  prev.addEventListener('click', () => { if (page > 0) { page--; void render(); } });
  next.addEventListener('click', () => { if ((page + 1) * pageSize < total) { page++; void render(); } });
  for (const id of ['windows-backup-startup', 'windows-backup-toolbar']) {
    byId(id).addEventListener('click', () => { status.textContent = ''; dialog.showModal(); });
  }
  dialog.addEventListener('close', () => {
    if (dialog.open) return;
    ++generation;
    picker.value = '';
    release()?.catch(() => {});
  });
})();
