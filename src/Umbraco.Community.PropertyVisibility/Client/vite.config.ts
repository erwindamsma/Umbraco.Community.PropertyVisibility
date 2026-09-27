import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { defineConfig, type Plugin, type Rollup } from 'vite';

const OUT_DIR = '../wwwroot/App_Plugins/UmbracoCommunityPropertyVisibility';
const MANIFEST_FILE = 'umbraco-package.json';

/**
 * The modules generated from the @hey-api/openapi-ts client templates (MIT, Copyright (c) Hey API): src/api/core and
 * src/api/client. Not src/api/client.gen.ts, index.ts, sdk.gen.ts or types.gen.ts, which are generated from this
 * package's own OpenAPI document.
 */
const HEY_API_TEMPLATE_MODULE = /[\\/]src[\\/]api[\\/](?:core|client)[\\/][^\\/]+\.ts$/;

/**
 * Everything of the generated API client (src/api and its runtime configuration in src/hey-api.ts). Bundled into one
 * chunk with a fixed name, `client.gen-<hash>.js`, which THIRD-PARTY-NOTICES.md names; without it the chunk would be
 * named after whichever module rollup happens to group with it.
 */
const API_CLIENT_MODULE = /[\\/]src[\\/](?:api[\\/].+|hey-api)\.ts$/;
const API_CLIENT_CHUNK = 'client.gen';

/**
 * The copyright and permission notice the MIT licence requires in copies and substantial portions, for every built
 * chunk that contains Hey API template code. THIRD-PARTY-NOTICES.md (repository root, packed into the nupkg) carries
 * the same notice.
 */
const HEY_API_BANNER = `/*!
 * Portions (c) Hey API, MIT licence, see THIRD-PARTY-NOTICES.md: the @hey-api/openapi-ts client templates
 * (src/api/core and src/api/client). Their copyright and permission notice:
 *
 * MIT License
 *
 * Copyright (c) Hey API
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */`;

/**
 * Prepends the Hey API licence banner to every chunk with Hey API template code. Fails the build when no chunk has that
 * code (a module layout that no longer matches HEY_API_TEMPLATE_MODULE must not ship the code without its notice) or
 * when the code lands in a chunk other than the one THIRD-PARTY-NOTICES.md names.
 *
 * In generateBundle, after minification: rollup's `output.banner` does not survive, because Vite runs esbuild with
 * `legalComments: 'none'` on every chunk. The chunk's source map, when there is one (development builds), is shifted by
 * the banner's lines.
 */
function heyApiLicenceBanner(): Plugin {
	const prefix = `${HEY_API_BANNER}\n`;
	const prefixLines = prefix.split('\n').length - 1;

	return {
		name: 'hey-api-licence-banner',
		apply: 'build',
		enforce: 'post',
		generateBundle(_options, bundle) {
			const chunks = Object.values(bundle).filter(
				(output): output is Rollup.OutputChunk =>
					output.type === 'chunk' && output.moduleIds.some((id) => HEY_API_TEMPLATE_MODULE.test(id)),
			);
			if (chunks.length === 0) {
				this.error('No chunk contains the Hey API client templates; check HEY_API_TEMPLATE_MODULE in vite.config.ts.');
			}

			for (const chunk of chunks) {
				if (chunk.name !== API_CLIENT_CHUNK) {
					this.error(`${chunk.fileName} has Hey API template code; THIRD-PARTY-NOTICES.md expects it in ${API_CLIENT_CHUNK}-<hash>.js.`);
				}
				chunk.code = prefix + chunk.code;
				if (!chunk.map) continue;
				chunk.map.mappings = ';'.repeat(prefixLines) + chunk.map.mappings;
				const mapFile = chunk.sourcemapFileName ? bundle[chunk.sourcemapFileName] : undefined;
				if (mapFile?.type === 'asset') mapFile.source = chunk.map.toString();
			}
		},
	};
}

/**
 * Writes the package version into the copied umbraco-package.json.
 * The version drives backoffice telemetry and, from Umbraco 17.6, the `?umb__rnd` cache busting of the bundle URL.
 * The MSBuild BuildClient target passes PACKAGE_VERSION=$(Version); a plain `npm run build` falls back to 0.0.0.
 */
function umbracoPackageVersion(): Plugin {
	return {
		name: 'umbraco-package-version',
		apply: 'build',
		closeBundle() {
			const version = process.env.PACKAGE_VERSION?.trim() || '0.0.0';
			const file = join(OUT_DIR, MANIFEST_FILE);
			const manifest = JSON.parse(readFileSync(file, 'utf8')) as { version?: string };
			manifest.version = version;
			writeFileSync(file, JSON.stringify(manifest, null, '\t') + '\n');
			this.info(`${MANIFEST_FILE} version set to ${version}`);
		},
	};
}

export default defineConfig(({ mode }) => ({
	plugins: [umbracoPackageVersion(), heyApiLicenceBanner()],
	build: {
		lib: {
			entry: 'src/bundle.manifests.ts',
			formats: ['es'],
			fileName: 'property-visibility',
		},
		outDir: OUT_DIR,
		emptyOutDir: true,
		sourcemap: mode !== 'production',
		rollupOptions: {
			external: [/^@umbraco/],
			output: {
				manualChunks: (id) => (API_CLIENT_MODULE.test(id) ? API_CLIENT_CHUNK : undefined),
			},
		},
	},
}));
