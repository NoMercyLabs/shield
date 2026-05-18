#!/usr/bin/env node
// Pre-build lint for the i18n catalog. Catches the two landmines that bit us this
// session — bare @ characters that vue-i18n parses as link references, and message
// compile failures the runtime would otherwise turn into "SyntaxError: 10". Run via
// `npm run lint:locales` or as part of `npm run build` (wired into the build script).
import fs from 'node:fs'
import path from 'node:path'
import { baseCompile } from '@intlify/message-compiler'

const localesDir = path.resolve('src/i18n/locales')
let failed = 0

for (const file of fs.readdirSync(localesDir)) {
  if (!file.endsWith('.json')) continue
  const data = JSON.parse(fs.readFileSync(path.join(localesDir, file), 'utf8'))
  walk(file, '', data)
}

if (failed === 0) {
  console.log('i18n: all messages compile cleanly.')
  process.exit(0)
}
console.error(`i18n: ${failed} bad message(s) — fix before build.`)
process.exit(1)

function walk(file, prefix, obj) {
  if (typeof obj === 'string') {
    const errors = []
    baseCompile(obj, { onError: (e) => errors.push(e) })
    for (const e of errors) {
      failed++
      console.error(`  ${file}  ${prefix}\n    err: code=${e.code} ${e.message}\n    src: ${obj.slice(0, 160)}\n`)
    }
  }
  else if (obj && typeof obj === 'object') {
    for (const k of Object.keys(obj)) walk(file, prefix ? `${prefix}.${k}` : k, obj[k])
  }
}
