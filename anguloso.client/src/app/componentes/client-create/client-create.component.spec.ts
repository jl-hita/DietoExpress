import { beforeEach, describe, expect, it } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ClientCreateComponent } from './client-create.component';
import { flushPendingHttpRequests } from '../../testing/http-test-flusher';

describe('ClientCreateComponent', () => {
    let component: ClientCreateComponent;
    let fixture: ComponentFixture<ClientCreateComponent>;
    let httpTesting: HttpTestingController;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [ClientCreateComponent],
            providers: [provideHttpClient(), provideHttpClientTesting()]
        })
            .compileComponents();

        fixture = TestBed.createComponent(ClientCreateComponent);
        component = fixture.componentInstance;
        httpTesting = TestBed.inject(HttpTestingController);
        fixture.detectChanges();
        flushPendingHttpRequests(httpTesting, fixture);
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });
});
