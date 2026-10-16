import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { LoginComponent } from './login.component';
import { flushPendingHttpRequests } from '../../testing/http-test-flusher';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let httpTesting: HttpTestingController;
  let originalGoogle: unknown;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    })
    .compileComponents();

    originalGoogle = (window as any).google;
    (window as any).google = { accounts: { id: { initialize: () => undefined, renderButton: () => undefined } } };
    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    httpTesting = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    flushPendingHttpRequests(httpTesting, fixture);
  });

  afterEach(() => {
    httpTesting.verify();
    if (originalGoogle === undefined) delete (window as any).google;
    else (window as any).google = originalGoogle;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
