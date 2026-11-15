<?php

declare(strict_types=1);

return [
	'routes' => [
		// Seite mit dem Designer (im iframe)
		['name' => 'page#index', 'url' => '/', 'verb' => 'GET'],
		// Dateien des Designers (WebAssembly) aus editor/
		['name' => 'editor#file', 'url' => '/editor/{path}', 'verb' => 'GET', 'requirements' => ['path' => '.+']],
		// Zugriffe auf Immich über den Nextcloud-Server (nur der eingetragene Server, nur bestimmte Pfade)
		['name' => 'immich#get', 'url' => '/immich/api/{path}', 'verb' => 'GET', 'requirements' => ['path' => '.+']],
		['name' => 'immich#post', 'url' => '/immich/api/{path}', 'verb' => 'POST', 'requirements' => ['path' => '.+']],
	],
];
