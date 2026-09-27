import {build} from 'esbuild';
import {mkdir,copyFile,cp,readFile} from 'node:fs/promises';
const version=process.env.NYANG_BUILD_VERSION||JSON.parse((await readFile('../version.json','utf8')).replace(/^\uFEFF/,'')).version;
await mkdir('../dist/ui',{recursive:true});
await build({entryPoints:['src/main.jsx'],bundle:true,minify:true,outdir:'../dist/ui',entryNames:'app',external:['/fonts/*'],define:{__APP_VERSION__:JSON.stringify(version),'process.env.NODE_ENV':'"production"'},target:'chrome110'});
await copyFile('index.html','../dist/ui/index.html');
await cp('public','../dist/ui',{recursive:true});
