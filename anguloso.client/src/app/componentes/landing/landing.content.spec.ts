import { describe, expect, it } from "vitest";

import {
    LANDING_CONTENT,
    LandingContent,
    publishableScreenshots,
    publishableTestimonials,
    publishableVideo,
    validateLandingContent
} from './landing.content';

const empty: LandingContent = { video: null, screenshots: [], testimonials: [] };

describe('LANDING_CONTENT (lo que se publica)', () => {
    it('has no problems: nothing can be published without accessible text, own assets and consent', () => {
        expect(validateLandingContent(LANDING_CONTENT)).toEqual([]);
    });

    it('renders no section while it is empty', () => {
        expect(publishableVideo(LANDING_CONTENT)).toBeNull();
        expect(publishableScreenshots(LANDING_CONTENT)).toEqual([]);
        expect(publishableTestimonials(LANDING_CONTENT)).toEqual([]);
    });
});

describe('validateLandingContent', () => {
    it('accepts well-formed content', () => {
        const content: LandingContent = {
            video: { src: '/assets/landing/demo.mp4', title: 'Demostración', poster: '/assets/landing/demo.jpg', captionsSrc: '/assets/landing/demo.vtt' },
            screenshots: [{ src: '/assets/landing/agenda.webp', alt: 'Agenda semanal con citas' }],
            testimonials: [{ quote: 'Me ahorra horas.', name: 'Laura', role: 'Dietista-nutricionista', consentGiven: true }]
        };
        expect(validateLandingContent(content)).toEqual([]);
    });

    it('rejects external or traversing URLs (the security policy only allows own media)', () => {
        const problems = validateLandingContent({
            ...empty,
            video: { src: 'https://youtube.com/watch?v=x', title: 'Demo' },
            screenshots: [
                { src: 'https://cdn.example.com/a.png', alt: 'a' },
                { src: '/assets/landing/../secreto.png', alt: 'b' }
            ]
        });
        expect(problems.length).toBe(3);
    });

    it('requires an accessible description for screenshots, video and photos', () => {
        const problems = validateLandingContent({
            video: { src: '/assets/landing/demo.mp4', title: ' ' },
            screenshots: [{ src: '/assets/landing/a.webp', alt: '' }],
            testimonials: [{ quote: 'Genial', name: 'Ana', role: 'Nutricionista', photo: '/assets/landing/ana.jpg', consentGiven: true }]
        });
        expect(problems.some(p => p.includes('title'))).toBe(true);
        expect(problems.some(p => p.includes('alt'))).toBe(true);
        expect(problems.some(p => p.includes('photoAlt'))).toBe(true);
    });

    it('flags a testimonial without express consent', () => {
        const problems = validateLandingContent({ ...empty, testimonials: [{ quote: 'Genial', name: 'Ana', role: 'Nutricionista', consentGiven: false }] });
        expect(problems.some(p => p.includes('consentGiven'))).toBe(true);
    });
});

describe('what the page is allowed to show', () => {
    it('never shows a testimonial without consent, even if it is in the list', () => {
        const shown = publishableTestimonials({
            ...empty,
            testimonials: [
                { quote: 'A', name: 'Ana', role: 'Nutricionista', consentGiven: false },
                { quote: 'B', name: 'Luis', role: 'Dietista', consentGiven: true }
            ]
        });
        expect(shown.map(t => t.name)).toEqual(['Luis']);
    });

    it('skips screenshots without description or with a foreign source', () => {
        const shown = publishableScreenshots({
            ...empty,
            screenshots: [
                { src: '/assets/landing/ok.webp', alt: 'Correcta' },
                { src: '/assets/landing/sin-alt.webp', alt: '' },
                { src: 'https://x.test/a.png', alt: 'Externa' }
            ]
        });
        expect(shown.map(s => s.alt)).toEqual(['Correcta']);
    });

    it('skips a video that is not self-hosted or has no title', () => {
        expect(publishableVideo({ ...empty, video: { src: 'https://x.test/v.mp4', title: 'Demo' } })).toBeNull();
        expect(publishableVideo({ ...empty, video: { src: '/assets/landing/v.mp4', title: '' } })).toBeNull();
        expect(publishableVideo({ ...empty, video: { src: '/assets/landing/v.mp4', title: 'Demo' } })?.title).toBe('Demo');
    });
});
