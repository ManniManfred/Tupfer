// Dateien-App: Tupfer-Projekte (*.tupfer.json) per Klick im Tupfer Designer öffnen und über das Menü
// „Neu“ anlegen.
// Ohne Build-Schritt: Die Aktion wird so angemeldet wie mit registerFileAction aus @nextcloud/files –
// Version 3 (Nextcloud bis 32) und Version 4 (ab 33), die Funktionen verstehen beide Aufrufarten.
(function () {
	const icon = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='currentColor'><path d='M17.6 2.9a2.4 2.4 0 0 1 3.4 0l.1.1a2.4 2.4 0 0 1 0 3.4l-9 9-3.5-3.5z'/><path d='M7.9 12.6l3.5 3.5-2.4 1-.9.4a.6.6 0 0 1-.8-.8l.3-.8z'/><circle cx='6.6' cy='18.4' r='1.4'/><path d='M14.6 17.9a6.5 6.5 0 0 0 3.6-3.6M16.9 21a10 10 0 0 0 4.4-4.4' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round'/><g opacity='.75'><circle cx='2.5' cy='9.5' r='1'/><circle cx='2.5' cy='13' r='1'/><circle cx='2.5' cy='16.5' r='1'/><circle cx='2.5' cy='20' r='1'/><circle cx='6' cy='9.5' r='1'/><circle cx='6' cy='13' r='1'/><circle cx='9.5' cy='20.5' r='1'/><circle cx='2.5' cy='6' r='1'/><circle cx='6' cy='6' r='1'/><circle cx='9.5' cy='9.5' r='1'/></g></svg>"
	// Version 3: (nodes, view) bzw. (node, view, dir); Version 4: ({ nodes, view, folder, contents })
	const nodesOf = (arg) => Array.isArray(arg) ? arg : (arg?.nodes ?? (arg ? [arg] : []))
	const action = {
		id: 'tupfer-open',
		displayName: () => 'Mit Tupfer Designer öffnen',
		iconSvgInline: () => icon,
		enabled: (arg) => {
			const nodes = nodesOf(arg)
			return nodes.length === 1 && nodes[0].type === 'file' && /\.tupfer\.json$/i.test(nodes[0].basename)
		},
		exec: async (arg) => {
			const node = nodesOf(arg)[0]
			window.location.href = OC.generateUrl('/apps/tupfer/') + '?datei=' + encodeURIComponent(node.path)
			return null
		},
		// Standardaktion beim Klick auf die Datei
		default: 'default',
		order: -50,
	}

	// @nextcloud/files 3
	window._nc_fileactions = window._nc_fileactions || []
	if (!window._nc_fileactions.some((a) => a.id === action.id)) {
		window._nc_fileactions.push(action)
	}

	// @nextcloud/files 4
	window._nc_files_scope = window._nc_files_scope || {}
	const scope = window._nc_files_scope.v4_0 = window._nc_files_scope.v4_0 || {}
	scope.fileActions = scope.fileActions || new Map()
	if (!scope.fileActions.has(action.id)) {
		scope.fileActions.set(action.id, action)
		// Ist die Dateien-App schon geladen, liest sie die Aktionen nach diesem Ereignis neu
		scope.registry?.dispatchEvent(new CustomEvent('register:action', { detail: action }))
	}

	// ---------- Menü „Neu“: Tupfer-Projekt ----------

	const entry = {
		id: 'tupfer-new',
		displayName: 'Tupfer-Projekt',
		iconSvgInline: icon,
		category: 1, // „Neu erstellen“
		order: 50,
		// Nur in Ordnern, in denen man Dateien anlegen darf
		enabled: (folder) => (folder.permissions & 4) !== 0,
		handler: (folder, content) => {
			const name = window.prompt('Name des neuen Tupfer-Projekts:', 'Neues Projekt')?.trim()
			if (!name) {
				return
			}
			const stem = name.replace(/\.tupfer\.json$/i, '').replace(/[\\/]/g, '_')
			const taken = new Set((content || []).map((node) => node.basename.toLowerCase()))
			let file = stem + '.tupfer.json'
			for (let i = 2; taken.has(file.toLowerCase()); i++) {
				file = `${stem} (${i}).tupfer.json`
			}
			const path = (folder.path || '/').replace(/\/$/, '') + '/' + file
			// Der Designer legt das Projekt an (Inhalt wie „Datei → Neu“) und speichert es gleich
			window.location.href = OC.generateUrl('/apps/tupfer/') + '?neu=' + encodeURIComponent(path)
		},
	}

	// Wie NewMenu aus @nextcloud/files, falls die Dateien-App das Menü noch nicht angelegt hat
	class NewMenu {
		_entries = []
		registerEntry(e) {
			if (this.getEntryIndex(e.id) !== -1) {
				throw new Error('Duplicate entry')
			}
			e.category = e.category ?? 1
			this._entries.push(e)
		}
		unregisterEntry(e) {
			const index = this.getEntryIndex(typeof e === 'string' ? e : e.id)
			if (index !== -1) {
				this._entries.splice(index, 1)
			}
		}
		getEntries(context) {
			return context
				? this._entries.filter((e) => typeof e.enabled === 'function' ? e.enabled(context) : true)
				: this._entries
		}
		getEntryIndex(id) {
			return this._entries.findIndex((e) => e.id === id)
		}
	}

	const addEntry = (menu) => {
		if (!menu.getEntries().some((e) => e.id === entry.id)) {
			menu.registerEntry(entry)
		}
	}
	// @nextcloud/files 3 (bis Nextcloud 32) und 4 (ab 33)
	window._nc_newfilemenu = window._nc_newfilemenu || new NewMenu()
	addEntry(window._nc_newfilemenu)
	scope.newFileMenu = scope.newFileMenu || new NewMenu()
	addEntry(scope.newFileMenu)
})()
