<?php

declare(strict_types=1);

namespace OCA\Tupfer\Controller;

use OCA\Tupfer\AppInfo\Application;
use OCP\AppFramework\Controller;
use OCP\AppFramework\Http;
use OCP\AppFramework\Http\Attribute\NoAdminRequired;
use OCP\AppFramework\Http\Attribute\NoCSRFRequired;
use OCP\AppFramework\Http\EmptyContentSecurityPolicy;
use OCP\AppFramework\Http\EmptyFeaturePolicy;
use OCP\AppFramework\Http\NotFoundResponse;
use OCP\AppFramework\Http\Response;
use OCP\AppFramework\Http\StreamResponse;
use OCP\IRequest;

/**
 * Liefert die Dateien des Designers (Web-Version, WebAssembly) aus editor/ aus. Über PHP statt direkt vom
 * Webserver, weil dessen Regeln für Nextcloud Dateien wie .html, .dat oder .json oft nicht ausliefern, und
 * damit der Designer eigene Sicherheitsregeln bekommt (WebAssembly, Sprachausgabe aus dem Netz).
 */
class EditorController extends Controller {
	private const TYPES = [
		'html' => 'text/html; charset=utf-8',
		'js' => 'text/javascript; charset=utf-8',
		'mjs' => 'text/javascript; charset=utf-8',
		'css' => 'text/css; charset=utf-8',
		'json' => 'application/json',
		'wasm' => 'application/wasm',
		'dat' => 'application/octet-stream',
		'png' => 'image/png',
		'svg' => 'image/svg+xml',
		'ico' => 'image/x-icon',
		'map' => 'application/json',
		'md' => 'text/markdown; charset=utf-8',
	];

	public function __construct(IRequest $request) {
		parent::__construct(Application::APP_ID, $request);
	}

	/** Verzeichnis mit der Web-Version (beim Bauen der App aus Tupfer.Designer.Browser kopiert). */
	private static function root(): string|false {
		return realpath(__DIR__ . '/../../editor');
	}

	public static function isInstalled(): bool {
		$root = self::root();
		return $root !== false && is_file($root . '/index.html');
	}

	#[NoAdminRequired]
	#[NoCSRFRequired]
	public function file(string $path): Response {
		$root = self::root();
		$file = $root === false ? false : realpath($root . '/' . $path);
		if ($file === false || !str_starts_with($file, $root . DIRECTORY_SEPARATOR) || !is_file($file)) {
			return new NotFoundResponse();
		}
		$extension = strtolower(pathinfo($file, PATHINFO_EXTENSION));
		$type = self::TYPES[$extension] ?? 'application/octet-stream';

		// Vorkomprimierte Fassung (vom Publish erzeugt), wenn der Browser sie versteht
		$accept = $this->request->getHeader('Accept-Encoding');
		$encoding = null;
		foreach (['br' => '.br', 'gzip' => '.gz'] as $name => $suffix) {
			if (str_contains($accept, $name) && is_file($file . $suffix)) {
				$encoding = $name;
				$file .= $suffix;
				break;
			}
		}

		$response = new StreamResponse($file);
		$response->addHeader('Content-Type', $type);
		$response->addHeader('Content-Length', (string)filesize($file));
		$response->addHeader('Vary', 'Accept-Encoding');
		if ($encoding !== null) {
			$response->addHeader('Content-Encoding', $encoding);
		}

		// Dateien mit Kennung im Namen (z. B. Avalonia.Base.j7ux3dfexy.wasm) ändern sich nie,
		// alle übrigen (u. a. dotnet.js mit der Liste der App-Dateien) immer neu prüfen
		if (preg_match('/\.[a-z0-9]{10}\.[a-z]+$/', $path) === 1) {
			$response->cacheFor(31536000, false, true);
		} else {
			$response->addHeader('Cache-Control', 'no-cache');
			$response->setETag(md5($file . filemtime($file) . filesize($file)));
		}

		$response->setContentSecurityPolicy(self::policy());
		$features = new EmptyFeaturePolicy();
		$features->addAllowedMicrophoneDomain('\'self\'');
		$features->addAllowedFullScreenDomain('\'self\'');
		$features->addAllowedAutoplayDomain('\'self\'');
		$response->setFeaturePolicy($features);
		return $response;
	}

	/**
	 * Eigene Sicherheitsregeln (eine EmptyContentSecurityPolicy wird von Nextcloud nicht mit den
	 * Standardregeln zusammengeführt): WebAssembly, meSpeak (eval), die natürliche Sprachausgabe
	 * (Piper von cdnjs, jsDelivr und Hugging Face) und die Einbettung in die Seite der App.
	 */
	private static function policy(): EmptyContentSecurityPolicy {
		$cdn = ['https://cdnjs.cloudflare.com', 'https://cdn.jsdelivr.net'];
		$voices = ['https://huggingface.co', 'https://*.huggingface.co', 'https://*.hf.co'];

		$csp = new EmptyContentSecurityPolicy();
		$csp->addAllowedScriptDomain('\'self\'');
		foreach ($cdn as $domain) {
			$csp->addAllowedScriptDomain($domain);
		}
		$csp->allowEvalScript(true);
		$csp->allowEvalWasm(true);
		$csp->allowInlineStyle(true);
		$csp->addAllowedStyleDomain('\'self\'');
		foreach (['\'self\'', 'data:', 'blob:'] as $domain) {
			$csp->addAllowedImageDomain($domain);
			$csp->addAllowedFontDomain($domain);
			$csp->addAllowedMediaDomain($domain);
		}
		foreach (array_merge(['\'self\'', 'data:', 'blob:'], $cdn, $voices) as $domain) {
			$csp->addAllowedConnectDomain($domain);
		}
		foreach (array_merge(['\'self\'', 'blob:'], $cdn) as $domain) {
			$csp->addAllowedWorkerSrcDomain($domain);
		}
		// Drucken: PDF-Anzeige im unsichtbaren iframe
		$csp->addAllowedFrameDomain('\'self\'');
		$csp->addAllowedFrameDomain('blob:');
		$csp->addAllowedObjectDomain('blob:');
		$csp->addAllowedFrameAncestorDomain('\'self\'');
		return $csp;
	}
}
