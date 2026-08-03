import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FoodTesterComponent } from './food-tester.component';

describe('FoodTesterComponent', () => {
  let component: FoodTesterComponent;
  let fixture: ComponentFixture<FoodTesterComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [FoodTesterComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(FoodTesterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
