import { dotnet } from './_framework/dotnet.js';
import * as tiptoy from './tiptoy.js';

// Avalonia übernimmt die Sicherheitsabstände des Geräts (env(safe-area-inset-*)) als Rand, vertauscht dabei
// aber unten und rechts. Firefox auf Android meldet unten einen Abstand – die App blieb rechts schmaler.
// Die Seite reicht ohne viewport-fit=cover ohnehin nicht unter Notch oder Navigationsleiste: Abstände auf 0.
{
    const setProperty = CSSStyleDeclaration.prototype.setProperty;
    CSSStyleDeclaration.prototype.setProperty = function (name, value, priority) {
        return setProperty.call(this, name, name.startsWith('--av-sa') ? '0px' : value, priority);
    };
}

const runtime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

runtime.setModuleImports('tiptoy', tiptoy);

const config = runtime.getConfig();
await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);

// Mit ?diagnose in der Adresse: Größenangaben des Browsers anzeigen (zur Fehlersuche bei der Darstellung)
if (new URLSearchParams(location.search).has('diagnose')) {
    const box = document.createElement('pre');
    box.style.cssText = 'position:fixed;left:0;right:0;bottom:0;margin:0;white-space:pre-wrap;padding:4px;font-size:11px;background:#ffffe0;opacity:.9;z-index:9;pointer-events:none';
    document.body.appendChild(box);
    setInterval(() => {
        const c = document.querySelector('canvas.avalonia-canvas');
        const v = window.visualViewport;
        box.textContent = `${navigator.userAgent}\n`
            + `Fenster ${innerWidth}×${innerHeight}, Seite ${document.documentElement.clientWidth}×${document.documentElement.clientHeight}, `
            + `scrollWidth ${document.documentElement.scrollWidth}, dpr ${devicePixelRatio}\n`
            + `visualViewport ${v ? `${Math.round(v.width)}×${Math.round(v.height)} scale ${v.scale}` : '–'}\n`
            + `Canvas ${c ? `${c.clientWidth}×${c.clientHeight} css, ${c.width}×${c.height} px` : '–'}`;
    }, 1000);
}
