// Seite der App: lädt den Designer in den iframe und reicht ?datei=… weiter.
(function () {
	const app = document.getElementById('tupfer-app')
	const frame = document.getElementById('tupfer-frame')
	if (!app || !frame) {
		return
	}
	// Hell/Dunkel wie in Nextcloud eingestellt (gilt im Designer bei „Wie System“); beim Theme
	// „Standard“ folgt Nextcloud dem Browser, dann folgt ihm auch der Designer von selbst
	const params = new URLSearchParams(location.search)
	const themes = (document.body.dataset.themes || '').split(',')
	if (themes.some(t => t.startsWith('dark'))) {
		params.set('darstellung', 'dunkel')
	} else if (themes.some(t => t.startsWith('light'))) {
		params.set('darstellung', 'hell')
	}
	const query = params.toString()
	frame.src = app.dataset.editor + (query ? '?' + query : '')
	// Nur einmal öffnen: Beim Neuladen soll der automatisch gesicherte Stand zurückkommen
	if (location.search) {
		history.replaceState(null, '', location.pathname)
	}
	frame.addEventListener('load', () => frame.focus())
})()
