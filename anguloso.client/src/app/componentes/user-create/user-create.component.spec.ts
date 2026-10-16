import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { flushPendingHttpRequests } from '../../testing/http-test-flusher';

import { UserCreateComponent } from './user-create.component';

describe('UserCreateComponent', () => {
  let component: UserCreateComponent;
  let fixture: ComponentFixture<UserCreateComponent>;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UserCreateComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    })
    .compileComponents();

    fixture = TestBed.createComponent(UserCreateComponent);
    component = fixture.componentInstance;
    httpTesting = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    flushPendingHttpRequests(httpTesting, fixture);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
