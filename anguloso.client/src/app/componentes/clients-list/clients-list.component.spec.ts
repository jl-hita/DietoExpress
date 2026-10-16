import { beforeEach, describe, expect, it } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ClientsListComponent } from './clients-list.component';
import { flushPendingHttpRequests } from '../../testing/http-test-flusher';

describe('ClientsListComponent', () => {
    let component: ClientsListComponent;
    let fixture: ComponentFixture<ClientsListComponent>;
    let httpTesting: HttpTestingController;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [ClientsListComponent],
            providers: [provideHttpClient(), provideHttpClientTesting()]
        })
            .compileComponents();

        fixture = TestBed.createComponent(ClientsListComponent);
        component = fixture.componentInstance;
        httpTesting = TestBed.inject(HttpTestingController);
        fixture.detectChanges();
        flushPendingHttpRequests(httpTesting, fixture);
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });
});
