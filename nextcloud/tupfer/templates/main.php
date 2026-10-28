<?php
/** @var array $_ */
/** @var \OCP\IL10N $l */
?>
<div id="tupfer-app" data-editor="<?php p($_['editorUrl']); ?>">
<?php if ($_['installed']): ?>
	<iframe id="tupfer-frame" title="Tupfer Designer" allow="microphone; fullscreen; autoplay; clipboard-read; clipboard-write"></iframe>
<?php else: ?>
	<div class="tupfer-missing">
		<h2>Tupfer Designer</h2>
		<p>Der Designer fehlt in dieser App (Ordner <code>editor/</code>). Bitte das fertige Paket
		<code>Tupfer-Designer-Nextcloud.tar.gz</code> aus den Releases installieren oder die App mit
		<code>nextcloud/build.sh</code> bauen.</p>
	</div>
<?php endif; ?>
</div>
