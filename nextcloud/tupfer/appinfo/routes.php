<?php

declare(strict_types=1);

return [
	'routes' => [
		// Seite mit dem Designer (im iframe)
		['name' => 'page#index', 'url' => '/', 'verb' => 'GET'],
		// Dateien des Designers (WebAssembly) aus editor/
		['name' => 'editor#file', 'url' => '/editor/{path}', 'verb' => 'GET', 'requirements' => ['path' => '.+']],
	],
];
