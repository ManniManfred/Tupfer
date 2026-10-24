import { dotnet } from './_framework/dotnet.js';
import * as tiptoy from './tiptoy.js';

const runtime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

runtime.setModuleImports('tiptoy', tiptoy);

const config = runtime.getConfig();
await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
