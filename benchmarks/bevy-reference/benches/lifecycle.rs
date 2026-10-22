use std::hint::black_box;

use bevy_reference::lifecycle::{
    COUNTS, despawn, empty_world, get, populated, shuffled, spawn, spawn_batch, tag_round_trip,
    wide_round_trip,
};
use criterion::{BatchSize, BenchmarkId, Criterion, Throughput, criterion_group, criterion_main};

fn lifecycle(c: &mut Criterion) {
    let mut group = c.benchmark_group("lifecycle");
    for count in COUNTS {
        group.throughput(Throughput::Elements(count as u64));

        group.bench_function(BenchmarkId::new("spawn", count), |b| {
            b.iter_batched_ref(
                || (empty_world(), Vec::with_capacity(count)),
                |(world, entities)| spawn(world, entities, count),
                BatchSize::PerIteration,
            )
        });

        group.bench_function(BenchmarkId::new("spawn_batch", count), |b| {
            b.iter_batched_ref(
                || (empty_world(), Vec::with_capacity(count)),
                |(world, entities)| spawn_batch(world, entities, count),
                BatchSize::PerIteration,
            )
        });

        group.bench_function(BenchmarkId::new("despawn", count), |b| {
            b.iter_batched_ref(
                || {
                    let (world, entities) = populated(count);
                    (world, shuffled(&entities))
                },
                |(world, entities)| despawn(world, entities),
                BatchSize::PerIteration,
            )
        });

        let (mut world, entities) = populated(count);
        let random = shuffled(&entities);
        group.bench_function(BenchmarkId::new("get", count), |b| {
            b.iter(|| get(black_box(&world), &random))
        });
        group.bench_function(BenchmarkId::new("tag_round_trip", count), |b| {
            b.iter(|| tag_round_trip(black_box(&mut world), &entities))
        });
        group.bench_function(BenchmarkId::new("wide_round_trip", count), |b| {
            b.iter(|| wide_round_trip(black_box(&mut world), &entities))
        });
    }
    group.finish();
}

criterion_group!(benches, lifecycle);
criterion_main!(benches);
