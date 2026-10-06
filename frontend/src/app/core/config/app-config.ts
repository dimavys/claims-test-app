import { Injectable } from '@angular/core';

export interface AppConfigValues {
  /** Origin of the Claims API, e.g. https://claims-api.azurewebsites.net (no trailing slash). */
  apiBaseUrl: string;
}

const DEFAULTS: AppConfigValues = { apiBaseUrl: 'http://localhost:5080' };

/**
 * Runtime configuration loaded from /config.json before the app starts, so the same build can be deployed to
 * any environment by editing one file (no rebuild).
 */
@Injectable({ providedIn: 'root' })
export class AppConfig {
  private values: AppConfigValues = DEFAULTS;

  get apiBaseUrl(): string {
    return this.values.apiBaseUrl.replace(/\/+$/, '');
  }

  async load(): Promise<void> {
    try {
      const response = await fetch('/config.json', { cache: 'no-store' });
      if (response.ok) {
        this.values = { ...DEFAULTS, ...(await response.json()) };
      }
    } catch {
      // Keep the defaults: an unreachable config file must not stop the app from booting.
    }
  }
}
