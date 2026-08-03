import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ConfirmarEmailComponent } from './confirmar-email.component';

describe('ConfirmarEmailComponent', () => {
  let component: ConfirmarEmailComponent;
  let fixture: ComponentFixture<ConfirmarEmailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ConfirmarEmailComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ConfirmarEmailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
