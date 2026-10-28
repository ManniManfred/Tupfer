<?php

declare(strict_types=1);

namespace OCA\Tupfer\Listener;

use OCA\Files\Event\LoadAdditionalScriptsEvent;
use OCA\Tupfer\AppInfo\Application;
use OCP\EventDispatcher\Event;
use OCP\EventDispatcher\IEventListener;
use OCP\Util;

/** @template-implements IEventListener<LoadAdditionalScriptsEvent> */
class LoadFilesScripts implements IEventListener {
	public function handle(Event $event): void {
		if (!$event instanceof LoadAdditionalScriptsEvent) {
			return;
		}
		// Vor der Dateien-App laden, damit die Dateiaktion schon angemeldet ist
		Util::addInitScript(Application::APP_ID, 'tupfer-files');
	}
}
