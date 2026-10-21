use std::hint::black_box;

use bevy_reference::{Handwritten, Scheduled, for_each, for_loop};
use criterion::{BenchmarkId, Criterion, Throughput, criterion_group, criterion_main};

const COUNT: usize = 100_000;

fn movement(c: &mut Criterion) {
    let mut group = c.benchmark_group("movement");
    group.throughput(Throughput::Elements(COUNT as u64));

    let mut handwritten = Handwritten::new(COUNT);
    group.bench_function(BenchmarkId::new("handwritten", COUNT), |b| {
        b.iter(|| black_box(&mut handwritten).run())
    });

    let mut each = Scheduled::new(COUNT, for_each);
    group.bench_function(BenchmarkId::new("bevy_for_each", COUNT), |b| {
        b.iter(|| black_box(&mut each).run())
    });

    let mut query = Scheduled::new(COUNT, for_loop);
    group.bench_function(BenchmarkId::new("bevy_for_loop", COUNT), |b| {
        b.iter(|| black_box(&mut query).run())
    });

    group.finish();
}

criterion_group!(benches, movement);
criterion_main!(benches);
