import { bindings, defineConfig } from "cf/config";
import * as entrypoint from "./src/index.ts" with { type: "cf-worker" };
export default defineConfig({ accountId: "61ebf28f815017c5a42d231de6652f3d", worker: { name: "omnia-metadata-auth", entrypoint, compatibilityDate: "2026-10-18", workersDev: true, env: { DB: bindings.d1({ name: "omnia-metadata-auth-db", id: "4209228b-3be5-49bd-9a0a-8a0164437ec5" }) } } });
