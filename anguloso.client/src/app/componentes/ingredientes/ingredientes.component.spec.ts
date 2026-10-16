import { beforeEach, describe, expect, it } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { IngredientesComponent } from './ingredientes.component';
import { flushPendingHttpRequests } from '../../testing/http-test-flusher';

describe('IngredientesComponent', () => {
    let component: IngredientesComponent;
    let fixture: ComponentFixture<IngredientesComponent>;
    let httpTesting: HttpTestingController;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [IngredientesComponent],
            providers: [provideHttpClient(), provideHttpClientTesting()]
        })
            .compileComponents();

        fixture = TestBed.createComponent(IngredientesComponent);
        component = fixture.componentInstance;
        httpTesting = TestBed.inject(HttpTestingController);
        fixture.detectChanges();
        flushPendingHttpRequests(httpTesting, fixture);
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });
});
