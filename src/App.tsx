import { useMemo, useRef, useState, type ChangeEvent } from 'react'
import { ChevronDown, ChevronRight, CircleHelp, FileImage, FolderOpen, HardDrive, MoreHorizontal, Search, ShieldCheck, Trash2, X } from 'lucide-react'
import './App.css'

type DuplicateFile = { id: string; name: string; path: string; size: string; date: string; bytes: number; selected: boolean; recommended: boolean }
type DuplicateGroup = { id: number; kind: string; name: string; copies: number; reclaim: string; reclaimBytes: number; files: DuplicateFile[] }

const formatBytes = (bytes: number) => {
  if (bytes < 1024) return `${bytes} B`
  const units = ['KB', 'MB', 'GB']
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)) - 1, units.length - 1)
  return `${(bytes / 1024 ** (exponent + 1)).toFixed(exponent === 0 ? 0 : 1)} ${units[exponent]}`
}

const hashFile = async (file: File) => {
  const buffer = await file.arrayBuffer()
  const digest = await crypto.subtle.digest('SHA-256', buffer)
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('')
}

const initialGroups: DuplicateGroup[] = [
  { id: 1, kind: 'PNG image', name: 'Alpine sunset.png', copies: 3, reclaim: '18.4 MB', reclaimBytes: 19293798, files: [
    { id: 'a1', name: 'Alpine sunset.png', path: 'Pictures / Travel / Alps', size: '9.2 MB', bytes: 9646899, date: 'Today, 10:42 AM', selected: false, recommended: true },
    { id: 'a2', name: 'Alpine sunset (1).png', path: 'Downloads', size: '9.2 MB', bytes: 9646899, date: 'Sep 25, 2026', selected: true, recommended: false },
    { id: 'a3', name: 'Alpine sunset copy.png', path: 'Desktop', size: '9.2 MB', bytes: 9646899, date: 'Sep 25, 2026', selected: true, recommended: false },
  ] },
  { id: 2, kind: 'MP4 video', name: 'Product walk-through.mp4', copies: 2, reclaim: '842.7 MB', reclaimBytes: 883634995, files: [
    { id: 'b1', name: 'Product walk-through.mp4', path: 'Videos / Work', size: '842.7 MB', bytes: 883634995, date: 'Sep 23, 2026', selected: false, recommended: true },
    { id: 'b2', name: 'Product walk-through final.mp4', path: 'Downloads', size: '842.7 MB', bytes: 883634995, date: 'Sep 24, 2026', selected: true, recommended: false },
  ] },
  { id: 3, kind: 'PDF document', name: 'Northwind proposal.pdf', copies: 2, reclaim: '4.1 MB', reclaimBytes: 4299161, files: [
    { id: 'c1', name: 'Northwind proposal.pdf', path: 'Documents / Clients', size: '4.1 MB', bytes: 4299161, date: 'Sep 18, 2026', selected: false, recommended: true },
    { id: 'c2', name: 'Northwind proposal v2.pdf', path: 'Desktop', size: '4.1 MB', bytes: 4299161, date: 'Sep 20, 2026', selected: true, recommended: false },
  ] },
]

function App() {
  const [groups, setGroups] = useState(initialGroups)
  const [expanded, setExpanded] = useState<number[]>([1])
  const [query, setQuery] = useState('')
  const [notice, setNotice] = useState('')
  const [rootFolder, setRootFolder] = useState('')
  const [isScanScopeOpen, setIsScanScopeOpen] = useState(false)
  const [isScanning, setIsScanning] = useState(false)
  const folderInputRef = useRef<HTMLInputElement>(null)
  const visibleGroups = useMemo(() => groups.filter((group) => group.name.toLowerCase().includes(query.toLowerCase())), [groups, query])
  const selectedCount = groups.flatMap((group) => group.files).filter((file) => file.selected).length
  const totalReclaimBytes = groups.reduce((total, group) => total + group.reclaimBytes, 0)
  const selectedReclaimBytes = groups.flatMap((group) => group.files).filter((file) => file.selected).reduce((total, file) => total + file.bytes, 0)
  const toggleFile = (groupId: number, fileId: string) => setGroups((current) => current.map((group) => group.id === groupId ? { ...group, files: group.files.map((file) => file.id === fileId ? { ...file, selected: !file.selected } : file) } : group))
  const toggleGroup = (groupId: number) => setExpanded((current) => current.includes(groupId) ? current.filter((id) => id !== groupId) : [...current, groupId])
  const scanFolder = async (event: ChangeEvent<HTMLInputElement>) => {
    const selectedFiles = Array.from(event.target.files ?? [])
    if (!selectedFiles.length) return

    setIsScanning(true)
    setNotice(`Scanning ${selectedFiles.length} files...`)
    const filesBySize = new Map<number, File[]>()
    selectedFiles.forEach((file) => filesBySize.set(file.size, [...(filesBySize.get(file.size) ?? []), file]))
    const filesByHash = new Map<string, File[]>()

    for (const files of filesBySize.values()) {
      if (files.length < 2) continue
      for (const file of files) {
        const hash = await hashFile(file)
        filesByHash.set(hash, [...(filesByHash.get(hash) ?? []), file])
      }
    }

    const duplicateGroups = Array.from(filesByHash.values()).filter((files) => files.length > 1).map((files, index) => {
      const sortedFiles = [...files].sort((first, second) => second.lastModified - first.lastModified)
      const bytes = sortedFiles[0].size
      const fileDetails = sortedFiles.map((file, fileIndex) => {
        const relativePath = (file as File & { webkitRelativePath?: string }).webkitRelativePath || file.name
        const pathSegments = relativePath.split('/')
        return { id: `${index}-${fileIndex}-${file.name}`, name: file.name, path: pathSegments.slice(0, -1).join(' / ') || rootFolder || 'Selected folder', size: formatBytes(file.size), bytes: file.size, date: new Date(file.lastModified).toLocaleDateString(), selected: fileIndex > 0, recommended: fileIndex === 0 }
      })
      const extension = sortedFiles[0].name.split('.').pop()?.toUpperCase() || 'File'
      return { id: index + 1, kind: `${extension} file`, name: sortedFiles[0].name, copies: sortedFiles.length, reclaim: formatBytes(bytes * (sortedFiles.length - 1)), reclaimBytes: bytes * (sortedFiles.length - 1), files: fileDetails }
    })

    const root = ((selectedFiles[0] as File & { webkitRelativePath?: string }).webkitRelativePath || '').split('/')[0]
    setRootFolder(root || 'Selected folder')
    setGroups(duplicateGroups)
    setExpanded(duplicateGroups.map((group) => group.id))
    setNotice(`Scan complete for ${root || 'the selected folder'}. Found ${duplicateGroups.length} duplicate ${duplicateGroups.length === 1 ? 'group' : 'groups'}.`)
    setIsScanning(false)
    setIsScanScopeOpen(false)
    event.target.value = ''
  }
  const removeSelected = () => {
    if (!selectedCount) return
    setGroups((current) => current.map((group) => ({ ...group, files: group.files.filter((file) => !file.selected) })).filter((group) => group.files.length > 1))
    setNotice(`${selectedCount} duplicate ${selectedCount === 1 ? 'file was' : 'files were'} moved to Recycle Bin.`)
  }

  return <main className="app-shell">
    <input className="hidden-directory-input" ref={(element) => { folderInputRef.current = element; element?.setAttribute('webkitdirectory', '') }} type="file" multiple onChange={scanFolder} />
    <aside className="sidebar"><div className="brand"><span className="brand-mark"><HardDrive size={19} /></span><span>Duplicate<br />Manager</span></div><button className="scan-button" onClick={() => setIsScanScopeOpen(true)}><FolderOpen size={18} /> Scan folders</button><nav aria-label="Main navigation"><p className="nav-label">LIBRARY</p><a className="nav-item active" href="#duplicates"><FileImage size={18} /> Duplicate files <span>7</span></a><a className="nav-item" href="#review"><ShieldCheck size={18} /> Smart review</a><p className="nav-label space">FOLDERS</p><a className="nav-item" href="#pictures"><FolderOpen size={18} /> Pictures</a><a className="nav-item" href="#documents"><FolderOpen size={18} /> Documents</a><a className="nav-item" href="#downloads"><FolderOpen size={18} /> Downloads</a></nav><div className="storage-card"><div><span>Available space</span><strong>146 GB</strong></div><div className="storage-meter"><span /></div><small>of 512 GB on this PC</small></div><button className="help-link"><CircleHelp size={17} /> Help &amp; support</button></aside>
    <section className="content" id="duplicates"><header className="topbar"><div><p className="eyebrow">DUPLICATE FILES</p><h1>Free up space, thoughtfully.</h1><p className="subhead">Review exact matches before removing anything.</p></div><div className="top-actions"><button className="icon-button" aria-label="More options"><MoreHorizontal size={21} /></button><button className="profile" aria-label="Account">JM</button></div></header>
      {isScanScopeOpen && <div className="scan-scope" role="dialog" aria-modal="true" aria-labelledby="scan-scope-title"><div className="scan-scope-heading"><div><p className="eyebrow">SCAN SCOPE</p><h2 id="scan-scope-title">Choose a root folder</h2></div><button className="scope-close" onClick={() => setIsScanScopeOpen(false)} aria-label="Close scan settings"><X size={18} /></button></div><p>The browser will ask for a folder. Only that folder and its subfolders will be scanned for exact file matches.</p><label className="root-input"><span>Current root</span><div><FolderOpen size={18} /><input value={rootFolder} readOnly placeholder="No folder selected" /></div></label><div className="scope-actions"><button className="clear-scope" onClick={() => setRootFolder('')}>Clear root</button><button className="scan-selected" onClick={() => folderInputRef.current?.click()} disabled={isScanning}><Search size={17} /> {isScanning ? 'Scanning files...' : 'Choose folder & scan'}</button></div></div>}
      <section className="summary" aria-label="Scan summary"><div className="summary-mark"><Trash2 size={26} /></div><div><p>Potential space to reclaim</p><strong>{formatBytes(totalReclaimBytes)}</strong></div><span className="summary-divider" /><div><p>Duplicate groups found</p><strong>{groups.length} groups</strong></div><button className="view-scan" onClick={() => setNotice(rootFolder ? `Last scan was limited to ${rootFolder}.` : 'Choose a root folder to begin a scan.')}>View last scan <ChevronRight size={16} /></button></section>
      {notice && <div className="notice"><ShieldCheck size={17} /> {notice}<button onClick={() => setNotice('')} aria-label="Dismiss notice"><X size={16} /></button></div>}
      <div className="list-toolbar"><div><h2>Review duplicates</h2><p>Keep one original from each group.</p></div><label className="search"><Search size={17} /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search files" /></label></div>
      <section className="duplicate-list" aria-label="Duplicate file groups">{visibleGroups.map((group) => { const isExpanded = expanded.includes(group.id); return <article className="duplicate-group" key={group.id}><button className="group-header" onClick={() => toggleGroup(group.id)} aria-expanded={isExpanded}><span className="file-icon"><FileImage size={19} /></span><span className="group-title"><strong>{group.name}</strong><small>{group.kind} · {group.copies} copies</small></span><span className="reclaim"><small>Reclaim</small><strong>{group.reclaim}</strong></span>{isExpanded ? <ChevronDown size={20} /> : <ChevronRight size={20} />}</button>{isExpanded && <div className="file-rows"><div className="table-head"><span>FILE</span><span>LOCATION</span><span>SIZE</span><span>MODIFIED</span><span /></div>{group.files.map((file) => <div className="file-row" key={file.id}><label className="check-wrap"><input type="checkbox" checked={file.selected} onChange={() => toggleFile(group.id, file.id)} /><span /></label><div className="file-name"><strong>{file.name}</strong>{file.recommended && <span className="keep-badge">Recommended to keep</span>}</div><span>{file.path}</span><span>{file.size}</span><span>{file.date}</span></div>)}</div>}</article> })}{visibleGroups.length === 0 && <p className="empty-state">No duplicate groups match your search.</p>}</section>
      <footer className="action-bar"><p>{selectedCount ? <><strong>{selectedCount} files selected</strong> · {formatBytes(selectedReclaimBytes)} to reclaim</> : 'Select duplicate copies to remove'}</p><button className="remove-button" disabled={!selectedCount} onClick={removeSelected}><Trash2 size={17} /> Remove selected</button></footer>
    </section>
  </main>
}

export default App
