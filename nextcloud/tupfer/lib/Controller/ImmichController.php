<?php

declare(strict_types=1);

namespace OCA\Tupfer\Controller;

use OCA\Tupfer\AppInfo\Application;
use OCP\AppFramework\Controller;
use OCP\AppFramework\Http;
use OCP\AppFramework\Http\Attribute\NoAdminRequired;
use OCP\AppFramework\Http\DataDisplayResponse;
use OCP\AppFramework\Http\JSONResponse;
use OCP\AppFramework\Http\Response;
use OCP\Http\Client\IClientService;
use OCP\IConfig;
use OCP\IRequest;

/**
 * Leitet die Zugriffe des Designers auf Immich über den Nextcloud-Server weiter. So braucht Immich keine
 * Freigabe für Zugriffe von anderen Seiten (CORS), und der Designer bleibt bei seinen Sicherheitsregeln.
 * Weitergeleitet wird nur an den vom Administrator eingetragenen Server
 * (occ config:app:set tupfer immich_url --value=https://fotos.example.org) und nur für die Pfade, die der
 * Designer braucht; sonst wäre die App ein offener Proxy. Den API-Key schickt der Designer im Header
 * x-api-key mit, er wird nur durchgereicht und nirgends gespeichert.
 */
class ImmichController extends Controller {
	private const ID = '[0-9a-fA-F-]{36}';

	/** Erlaubte Zugriffe: Methode und Pfad unter /api/ (ohne Abfrage). */
	private const ALLOWED = [
		['GET', '#^users/me$#'],
		['GET', '#^albums$#'],
		['GET', '#^people$#'],
		['GET', '#^people/' . self::ID . '/thumbnail$#'],
		['POST', '#^search/metadata$#'],
		['GET', '#^assets/' . self::ID . '$#'],
		['GET', '#^assets/' . self::ID . '/(thumbnail|original)$#'],
		['GET', '#^faces$#'],
	];

	public function __construct(
		IRequest $request,
		private IConfig $config,
		private IClientService $clientService,
	) {
		parent::__construct(Application::APP_ID, $request);
	}

	/** Eingetragene Adresse des Immich-Servers (ohne / und /api am Ende), leer wenn keine oder ungültig. */
	public static function serverUrl(IConfig $config): string {
		$url = rtrim(trim($config->getAppValue(Application::APP_ID, 'immich_url', '')), '/');
		if (str_ends_with(strtolower($url), '/api')) {
			$url = rtrim(substr($url, 0, -4), '/');
		}
		$parts = $url === '' ? false : parse_url($url);
		if ($parts === false || !isset($parts['scheme'], $parts['host']) || !in_array(strtolower($parts['scheme']), ['http', 'https'], true)) {
			return '';
		}
		return $url;
	}

	#[NoAdminRequired]
	public function get(string $path): Response {
		return $this->forward('GET', $path);
	}

	#[NoAdminRequired]
	public function post(string $path): Response {
		return $this->forward('POST', $path);
	}

	private function forward(string $method, string $path): Response {
		$server = self::serverUrl($this->config);
		if ($server === '') {
			return self::error(Http::STATUS_SERVICE_UNAVAILABLE, 'In dieser Nextcloud ist kein Immich-Server eingetragen.');
		}
		$allowed = false;
		foreach (self::ALLOWED as [$m, $pattern]) {
			if ($m === $method && preg_match($pattern, $path) === 1) {
				$allowed = true;
				break;
			}
		}
		if (!$allowed) {
			return self::error(Http::STATUS_NOT_FOUND, 'Dieser Zugriff auf Immich ist über Nextcloud nicht vorgesehen.');
		}
		$key = $this->request->getHeader('x-api-key');
		if ($key === '') {
			return self::error(Http::STATUS_UNAUTHORIZED, 'Es fehlt der API-Key.');
		}

		$query = parse_url($this->request->getRequestUri(), PHP_URL_QUERY);
		$url = $server . '/api/' . $path . (is_string($query) && $query !== '' ? '?' . $query : '');
		$options = [
			'headers' => ['x-api-key' => $key, 'Accept' => $this->request->getHeader('Accept') ?: '*/*'],
			'http_errors' => false,
			'timeout' => 60,
			// Immich läuft oft im eigenen Netz; die Adresse hat der Administrator selbst eingetragen
			'nextcloud' => ['allow_local_address' => true],
		];
		try {
			$client = $this->clientService->newClient();
			if ($method === 'POST') {
				$options['headers']['Content-Type'] = 'application/json';
				$options['body'] = (string)file_get_contents('php://input');
				$response = $client->post($url, $options);
			} else {
				$response = $client->get($url, $options);
			}
		} catch (\Throwable $e) {
			return self::error(Http::STATUS_BAD_GATEWAY, 'Der Immich-Server ist vom Nextcloud-Server aus nicht erreichbar: ' . $e->getMessage());
		}

		$body = $response->getBody();
		if (is_resource($body)) {
			$body = (string)stream_get_contents($body);
		}
		$type = $response->getHeader('Content-Type');
		$result = new DataDisplayResponse((string)$body, $response->getStatusCode(),
			['Content-Type' => $type !== '' ? $type : 'application/octet-stream']);
		$result->addHeader('Cache-Control', 'private, no-store');
		return $result;
	}

	/** Fehler im Format von Immich ({"message": …}), damit der Designer ihn wie dort anzeigt. */
	private static function error(int $status, string $message): JSONResponse {
		return new JSONResponse(['message' => $message, 'statusCode' => $status], $status);
	}
}
