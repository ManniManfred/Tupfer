<?php

declare(strict_types=1);

namespace OCA\Tupfer\Controller;

use OCA\Tupfer\AppInfo\Application;
use OCP\AppFramework\Controller;
use OCP\AppFramework\Http\Attribute\NoAdminRequired;
use OCP\AppFramework\Http\Attribute\NoCSRFRequired;
use OCP\AppFramework\Http\ContentSecurityPolicy;
use OCP\AppFramework\Http\FeaturePolicy;
use OCP\AppFramework\Http\TemplateResponse;
use OCP\IRequest;
use OCP\IURLGenerator;
use OCP\Util;

/** Seite der App: der Designer im iframe, darum herum die Kopfzeile von Nextcloud. */
class PageController extends Controller {
	public function __construct(
		IRequest $request,
		private IURLGenerator $urlGenerator,
	) {
		parent::__construct(Application::APP_ID, $request);
	}

	#[NoAdminRequired]
	#[NoCSRFRequired]
	public function index(): TemplateResponse {
		Util::addScript(Application::APP_ID, 'tupfer-main');
		Util::addStyle(Application::APP_ID, 'tupfer');

		$editorUrl = $this->urlGenerator->linkToRoute('tupfer.editor.file', ['path' => 'index.html']);
		$response = new TemplateResponse(Application::APP_ID, 'main', [
			'editorUrl' => $editorUrl,
			'installed' => EditorController::isInstalled(),
		]);

		$csp = new ContentSecurityPolicy();
		$csp->addAllowedFrameDomain('\'self\'');
		$response->setContentSecurityPolicy($csp);

		// Aufnahmen im Designer brauchen das Mikrofon
		$features = new FeaturePolicy();
		$features->addAllowedMicrophoneDomain('\'self\'');
		$features->addAllowedFullScreenDomain('\'self\'');
		$response->setFeaturePolicy($features);
		return $response;
	}
}
