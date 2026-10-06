import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';

export interface PublicSeoProfile {
  fullName: string;
  slug: string;
  clinicName: string;
  city: string;
  province: string;
  publicBio: string;
  specialties: string;
  onlineConsultations: boolean;
}

@Injectable({ providedIn: 'root' })
export class PublicSeoService {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly document = inject(DOCUMENT);

  setDirectorySeo(filters: { city: string; province: string; speciality: string; online: boolean }): void {
    const parts = [filters.speciality, filters.city, filters.province].map(value => value.trim()).filter(Boolean);
    const suffix = parts.length ? ` de ${parts.join(', ')}` : '';
    const onlineSuffix = filters.online ? ' con consulta online' : '';

    this.apply(
      `Nutricionistas${suffix}${onlineSuffix} | DietoExpress`,
      `Encuentra nutricionistas${suffix}${onlineSuffix}. Consulta perfiles profesionales y solicita cita en DietoExpress.`,
      '/nutricionistas',
      null
    );
  }

  setProfileSeo(profile: PublicSeoProfile): void {
    const location = [profile.city, profile.province].filter(value => value.trim()).join(', ');
    const specialty = profile.specialties.trim();
    const locationSuffix = location ? ` en ${location}` : '';
    const specialtySuffix = specialty ? ` — ${specialty}` : '';
    const onlineSuffix = profile.onlineConsultations ? ' Consulta online disponible.' : '';

    const description = this.truncate(
      profile.publicBio.trim() ||
      `Perfil profesional de ${profile.fullName}${specialtySuffix}${locationSuffix}.${onlineSuffix}`
    );

    const url = `/nutricionistas/${encodeURIComponent(profile.slug)}`;
    const jsonLd = {
      '@context': 'https://schema.org',
      '@type': 'Person',
      name: profile.fullName,
      url: this.absoluteUrl(url),
      ...(profile.clinicName ? { worksFor: { '@type': 'Organization', name: profile.clinicName } } : {}),
      ...(location ? { areaServed: { '@type': 'City', name: location } } : {}),
      ...(specialty ? { knowsAbout: specialty.split(',').map(value => value.trim()).filter(Boolean) } : {}),
      ...(profile.onlineConsultations ? { contactPoint: { '@type': 'ContactPoint', contactType: 'online consultation' } } : {})
    };

    this.apply(
      `${profile.fullName}${specialtySuffix}${locationSuffix} | DietoExpress`,
      description,
      url,
      jsonLd
    );
  }

  private apply(title: string, description: string, path: string, jsonLd: object | null): void {
    this.title.setTitle(title);
    this.meta.updateTag({ name: 'description', content: this.truncate(description) });
    this.meta.updateTag({ name: 'robots', content: 'index,follow' });
    this.meta.updateTag({ property: 'og:type', content: 'website' });
    this.meta.updateTag({ property: 'og:title', content: title });
    this.meta.updateTag({ property: 'og:description', content: this.truncate(description) });
    this.meta.updateTag({ property: 'og:url', content: this.absoluteUrl(path) });
    this.meta.updateTag({ name: 'twitter:card', content: 'summary' });
    this.meta.updateTag({ name: 'twitter:title', content: title });
    this.meta.updateTag({ name: 'twitter:description', content: this.truncate(description) });
    this.setCanonical(path);
    this.setJsonLd(jsonLd);
  }

  private setCanonical(path: string): void {
    const href = this.absoluteUrl(path);
    let link = this.document.head.querySelector<HTMLLinkElement>('link[data-dieto-canonical]');
    if (!link) {
      link = this.document.createElement('link');
      link.rel = 'canonical';
      link.setAttribute('data-dieto-canonical', 'true');
      this.document.head.appendChild(link);
    }
    link.href = href;
  }

  private setJsonLd(value: object | null): void {
    const existing = this.document.head.querySelector<HTMLScriptElement>('script[data-dieto-seo]');
    existing?.remove();
    if (!value) return;

    const script = this.document.createElement('script');
    script.type = 'application/ld+json';
    script.setAttribute('data-dieto-seo', 'true');
    // Evitamos que un valor textual termine interpretándose como una etiqueta HTML.
    script.textContent = JSON.stringify(value).replace(/</g, '\\u003c');
    this.document.head.appendChild(script);
  }

  private absoluteUrl(path: string): string {
    const origin = this.document.location?.origin || 'https://jlhitap.duckdns.org';
    return new URL(path, origin).toString();
  }

  private truncate(value: string): string {
    const normalized = value.replace(/\\s+/g, ' ').trim();
    return normalized.length <= 160 ? normalized : normalized.slice(0, 157).trimEnd() + '...';
  }
}
