import fsd from '@feature-sliced/steiger-plugin'
import { defineConfig } from 'steiger'

export default defineConfig([
  ...fsd.configs.recommended,
  // ponytail: Slices sind klein und flach; Segmente (ui/model) und "nur einmal genutzt"-Hinweise erst bei Wachstum.
  { rules: { 'fsd/no-segmentless-slices': 'off', 'fsd/insignificant-slice': 'off' } },
  // Nuxt-Routing: pages/ hat je Route eine Datei, kein index.
  { files: ['./app/pages/**'], rules: { 'fsd/no-layer-public-api': 'off' } },
])
