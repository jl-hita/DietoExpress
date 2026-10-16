import { beforeEach, describe, expect, it } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { UserResetComponent } from './user-reset.component';

describe('UserResetComponent', () => {
    let component: UserResetComponent;
    let fixture: ComponentFixture<UserResetComponent>;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [UserResetComponent],
            providers: [provideRouter([])]
        })
            .compileComponents();

        fixture = TestBed.createComponent(UserResetComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });
});
