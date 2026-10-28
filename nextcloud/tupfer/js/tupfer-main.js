// Seite der App: lädt den Designer in den iframe und reicht ?datei=… weiter.
(function () {
	const app = document.getElementById('tupfer-app')
	const frame = document.getElementById('tupfer-frame')
	if (!app || !frame) {
		return
	}
	frame.src = app.dataset.editor + location.search
	// Nur einmal öffnen: Beim Neuladen soll der automatisch gesicherte Stand zurückkommen
	if (location.search) {
		history.replaceState(null, '', location.pathname)
	}
	frame.addEventListener('load', () => frame.focus())
})()
